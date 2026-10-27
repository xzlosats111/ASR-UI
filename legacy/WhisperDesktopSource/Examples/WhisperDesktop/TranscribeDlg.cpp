#include "stdafx.h"
#include "TranscribeDlg.h"
#include "Utils/logger.h"

HRESULT TranscribeDlg::show()
{
	auto res = DoModal( nullptr );
	if( res == -1 )
		return HRESULT_FROM_WIN32( GetLastError() );
	switch( res )
	{
	case IDC_BACK:
		return SCREEN_MODEL;
	case IDC_CAPTURE:
		return SCREEN_CAPTURE;
	}
	return S_OK;
}

constexpr int progressMaxInteger = 1024 * 8;

static const LPCTSTR regValInput = L"sourceMedia";
static const LPCTSTR regValOutFormat = L"resultFormat";
static const LPCTSTR regValOutPath = L"resultPath";
static const LPCTSTR regValUseInputFolder = L"useInputFolder";

static const std::array<LPCTSTR, 4> batchOutputExtensions =
{
	L".txt", L".txt", L".srt", L".vtt"
};

static bool makeBatchOutputPath( CString& output, const CString& input, int formatIndex )
{
	if( formatIndex < 0 || formatIndex >= (int)batchOutputExtensions.size() )
		return false;
	output = input;
	output.Trim();
	const bool renamed = PathRenameExtension( output.GetBufferSetLength( output.GetLength() + 5 ), batchOutputExtensions[ formatIndex ] );
	output.ReleaseBuffer();
	return renamed;
}

namespace
{
	struct sTemporaryWav
	{
		CString path;
		~sTemporaryWav()
		{
			if( !path.IsEmpty() )
				DeleteFile( path );
		}
	};

	bool findFfmpeg( CString& path )
	{
		wchar_t modulePath[ MAX_PATH ];
		const DWORD length = GetModuleFileName( nullptr, modulePath, ARRAYSIZE( modulePath ) );
		if( length > 0 && length < ARRAYSIZE( modulePath ) && PathRemoveFileSpec( modulePath ) )
		{
			CString adjacent = modulePath;
			adjacent += L"\\ffmpeg.exe";
			if( PathFileExists( adjacent ) )
			{
				path = adjacent;
				return true;
			}
		}

		const DWORD needed = SearchPath( nullptr, L"ffmpeg.exe", nullptr, 0, nullptr, nullptr );
		if( needed == 0 )
			return false;
		std::vector<wchar_t> buffer( needed + 1 );
		if( SearchPath( nullptr, L"ffmpeg.exe", nullptr, (DWORD)buffer.size(), buffer.data(), nullptr ) == 0 )
			return false;
		path = buffer.data();
		return true;
	}

	HRESULT convertFlvToWav( const CString& source, CString& temporaryWav, CString& error )
	{
		CString ffmpeg;
		if( !findFfmpeg( ffmpeg ) )
		{
			error = L"无法读取 FLV：Windows 的媒体组件通常不支持 FLV。\n"
				L"请把 ffmpeg.exe 放到 WhisperDesktop.exe 同一目录，然后重试。";
			return HRESULT_FROM_WIN32( ERROR_FILE_NOT_FOUND );
		}

		wchar_t tempFolder[ MAX_PATH ];
		const DWORD folderLength = GetTempPath( ARRAYSIZE( tempFolder ), tempFolder );
		if( folderLength == 0 || folderLength >= ARRAYSIZE( tempFolder ) )
			return HRESULT_FROM_WIN32( GetLastError() );
		wchar_t tempName[ MAX_PATH ];
		if( GetTempFileName( tempFolder, L"wsp", 0, tempName ) == 0 )
			return HRESULT_FROM_WIN32( GetLastError() );

		CString wavPath = tempName;
		DeleteFile( wavPath );
		wchar_t* extensionBuffer = wavPath.GetBufferSetLength( wavPath.GetLength() + 5 );
		if( !PathRenameExtension( extensionBuffer, L".wav" ) )
		{
			wavPath.ReleaseBuffer();
			return E_FAIL;
		}
		wavPath.ReleaseBuffer();

		CString commandLine;
		commandLine.Format( L"\"%s\" -hide_banner -loglevel error -nostdin -y -i \"%s\" -vn -ac 1 -ar 16000 -f wav \"%s\"", cstr( ffmpeg ), cstr( source ), cstr( wavPath ) );
		std::vector<wchar_t> command( commandLine.GetLength() + 1 );
		wcscpy_s( command.data(), command.size(), commandLine );

		STARTUPINFO si;
		memset( &si, 0, sizeof( si ) );
		si.cb = sizeof( si );
		PROCESS_INFORMATION pi;
		memset( &pi, 0, sizeof( pi ) );
		if( !CreateProcess( nullptr, command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi ) )
			return HRESULT_FROM_WIN32( GetLastError() );
		CloseHandle( pi.hThread );
		WaitForSingleObject( pi.hProcess, INFINITE );
		DWORD exitCode = 1;
		GetExitCodeProcess( pi.hProcess, &exitCode );
		CloseHandle( pi.hProcess );
		if( exitCode != 0 || !PathFileExists( wavPath ) )
		{
			DeleteFile( wavPath );
			error.Format( L"FFmpeg 无法解码此 FLV 文件（退出代码 %u）。", exitCode );
			return E_FAIL;
		}

		temporaryWav = wavPath;
		return S_OK;
	}
}

LRESULT TranscribeDlg::OnInitDialog( UINT nMessage, WPARAM wParam, LPARAM lParam, BOOL& bHandled )
{
	// First DDX call, hooks up variables to controls.
	DoDataExchange( false );
	printModelDescription();
	languageSelector.initialize( m_hWnd, IDC_LANGUAGE, appState );
	cbConsole.initialize( m_hWnd, IDC_CONSOLE, appState );
	cbTranslate.initialize( m_hWnd, IDC_TRANSLATE, appState );
	populateOutputFormats();

	pendingState.initialize(
		{
			languageSelector, GetDlgItem( IDC_TRANSLATE ),
			sourceMediaPath, GetDlgItem( IDC_BROWSE_MEDIA ),
			transcribeOutFormat, useInputFolder,
			transcribeOutputPath, GetDlgItem( IDC_BROWSE_RESULT ),
			GetDlgItem( IDCANCEL ),
			GetDlgItem( IDC_BACK ),
			GetDlgItem( IDC_CAPTURE )
		},
		{
			progressBar, GetDlgItem( IDC_PENDING_TEXT )
		} );

	HRESULT hr = work.create( this );
	if( FAILED( hr ) )
	{
		reportError( m_hWnd, L"创建线程池工作失败", nullptr, hr );
		EndDialog( IDCANCEL );
	}

	progressBar.SetRange32( 0, progressMaxInteger );
	progressBar.SetStep( 1 );

	settingMediaPath = true;
	sourceMediaPath.SetWindowText( appState.stringLoad( regValInput ) );
	settingMediaPath = false;
	transcribeOutFormat.SetCurSel( (int)appState.dwordLoad( regValOutFormat, 3 ) );
	transcribeOutputPath.SetWindowText( appState.stringLoad( regValOutPath ) );
	if( appState.boolLoad( regValUseInputFolder ) )
		useInputFolder.SetCheck( BST_CHECKED );
	BOOL unused;
	onOutFormatChange( 0, 0, nullptr, unused );

	appState.lastScreenSave( SCREEN_TRANSCRIBE );
	appState.setupIcon( this );
	ATLVERIFY( CenterWindow() );
	return 0;
}

void TranscribeDlg::printModelDescription()
{
	CString text;
	if( S_OK == appState.model->isMultilingual() )
		text = L"多语言";
	else
		text = L"单语言";
	text += L" 模型 \"";
	LPCTSTR path = appState.source.path;
	path = ::PathFindFileName( path );
	text += path;
	text += L"\", 占用";
	const int64_t cb = appState.source.sizeInBytes;
	if( cb < 1 << 30 )
	{
		constexpr double mul = 1.0 / ( 1 << 20 );
		double mb = (double)cb * mul;
		text.AppendFormat( L"%.1f MB", mb );
	}
	else
	{
		constexpr double mul = 1.0 / ( 1 << 30 );
		double gb = (double)cb * mul;
		text.AppendFormat( L"%.2f GB", gb );
	}
	text += L", ";
	text += implString( appState.source.impl );
	text += L" 实现";

	modelDesc.SetWindowText( text );
}

// Populate the "Output Format" combobox
void TranscribeDlg::populateOutputFormats()
{
	transcribeOutFormat.AddString( L"无" );
	transcribeOutFormat.AddString( L"文本文件" );
	transcribeOutFormat.AddString( L"带时间戳的文本" );
	transcribeOutFormat.AddString( L"SubRip 字幕" );
	transcribeOutFormat.AddString( L"WebVTT 字幕" );
}

// The enum values should match 0-based indices of the combobox items
enum struct TranscribeDlg::eOutputFormat : uint8_t
{
	None = 0,
	Text = 1,
	TextTimestamps = 2,
	SubRip = 3,
	WebVTT = 4,
};

enum struct TranscribeDlg::eVisualState : uint8_t
{
	Idle = 0,
	Running = 1,
	Stopping = 2
};

// CBN_SELCHANGE notification for IDC_OUTPUT_FORMAT combobox
LRESULT TranscribeDlg::onOutFormatChange( UINT, INT, HWND, BOOL& bHandled )
{
	BOOL enabled = transcribeOutFormat.GetCurSel() != 0;
	useInputFolder.EnableWindow( enabled );

	if( isChecked( useInputFolder ) && enabled )
	{
		enabled = FALSE;
		setOutputPath();
	}
	transcribeOutputPath.EnableWindow( enabled );
	transcribeOutputBrowse.EnableWindow( enabled );

	return 0;
}

// EN_CHANGE notification for IDC_PATH_MEDIA edit box
LRESULT TranscribeDlg::onInputChange( UINT, INT, HWND, BOOL& )
{
	if( !settingMediaPath )
		selectedMediaPaths.clear();
	if( !useInputFolder.IsWindowEnabled() )
		return 0;
	if( !isChecked( useInputFolder ) )
		return 0;
	setOutputPath();
	return 0;
}

void TranscribeDlg::onBrowseMedia()
{
	LPCTSTR title = L"选择视频或音频文件（可按 Ctrl / Shift 多选）";
	LPCTSTR filters = L"视频或音频文件\0*.avi;*.mp4;*.mkv;*.mov;*.mpeg;*.mpeg4;*.mpg;*.webm;*.flv;*.wmv;*.wav;*.wave;*.mp3;*.wma;*.m4a;*.aac;*.flac;*.ogg\0所有文件 (*.*)\0*.*\0\0";

	CString initialPath;
	sourceMediaPath.GetWindowText( initialPath );
	std::vector<CString> picked;
	if( !getOpenFileNames( m_hWnd, title, filters, initialPath, picked ) )
		return;

	selectedMediaPaths.swap( picked );
	if( selectedMediaPaths.size() > 1 )
	{
		// One source produces one output. Same-folder output is unambiguous for a batch.
		useInputFolder.SetCheck( BST_CHECKED );
		onInputFolderCheck();
	}
	updateMediaSelectionText();
	if( selectedMediaPaths.size() == 1 && isChecked( useInputFolder ) )
		setOutputPath( selectedMediaPaths.front() );
	else if( selectedMediaPaths.size() > 1 )
		transcribeOutputPath.SetWindowText( L"批量模式：每个输出文件将保存到对应源文件的同一目录" );
}

void TranscribeDlg::updateMediaSelectionText()
{
	if( selectedMediaPaths.empty() )
		return;
	settingMediaPath = true;
	if( selectedMediaPaths.size() == 1 )
		sourceMediaPath.SetWindowText( selectedMediaPaths.front() );
	else
	{
		CString text;
		text.Format( L"已选择 %zu 个文件（第一个：%s）", selectedMediaPaths.size(), cstr( selectedMediaPaths.front() ) );
		sourceMediaPath.SetWindowText( text );
	}
	settingMediaPath = false;
}

static const LPCTSTR outputFilters = L"文本文件 (*.txt)\0*.txt\0带时间戳的文本 (*.txt)\0*.txt\0SubRip 字幕 (*.srt)\0*.srt\0WebVTT 字幕 (*.vtt)\0*.vtt\0\0";
static const std::array<LPCTSTR, 4> outputExtensions =
{
	L".txt", L".txt", L".srt", L".vtt"
};

void TranscribeDlg::setOutputPath( const CString& input )
{
	const int format = transcribeOutFormat.GetCurSel() - 1;
	if( format < 0 || format >= outputExtensions.size() )
		return;
	const LPCTSTR ext = outputExtensions[ format ];
	CString path = input;
	path.Trim();
	const bool renamed = PathRenameExtension( path.GetBufferSetLength( path.GetLength() + 4 ), ext );
	path.ReleaseBuffer();
	if( !renamed )
		return;
	transcribeOutputPath.SetWindowText( path );
}

void TranscribeDlg::setOutputPath()
{
	CString path;
	if( !sourceMediaPath.GetWindowText( path ) )
		return;
	if( path.GetLength() <= 0 )
		return;
	setOutputPath( path );
}

void TranscribeDlg::onInputFolderCheck()
{
	const bool checked = isChecked( useInputFolder );

	BOOL enableOutput = checked ? FALSE : TRUE;
	transcribeOutputPath.EnableWindow( enableOutput );
	transcribeOutputBrowse.EnableWindow( enableOutput );

	if( !checked )
		return;
	setOutputPath();
}

void TranscribeDlg::onBrowseOutput()
{
	const DWORD origFilterIndex = (DWORD)transcribeOutFormat.GetCurSel() - 1;

	LPCTSTR title = L"输出文本文件";
	CString path;
	transcribeOutputPath.GetWindowText( path );
	DWORD filterIndex = origFilterIndex;
	if( !getSaveFileName( m_hWnd, title, outputFilters, path, &filterIndex ) )
		return;

	LPCTSTR ext = PathFindExtension( path );
	if( 0 == *ext && filterIndex < outputExtensions.size() )
	{
		wchar_t* const buffer = path.GetBufferSetLength( path.GetLength() + 5 );
		PathRenameExtension( buffer, outputExtensions[ filterIndex ] );
		path.ReleaseBuffer();
	}

	transcribeOutputPath.SetWindowText( path );
	if( filterIndex != origFilterIndex )
		transcribeOutFormat.SetCurSel( filterIndex + 1 );
}

void TranscribeDlg::setPending( bool nowPending )
{
	pendingState.setPending( nowPending );
}

void TranscribeDlg::transcribeError( LPCTSTR text, HRESULT hr )
{
	reportError( m_hWnd, text, L"无法转写文件", hr );
}

void TranscribeDlg::onTranscribe()
{
	switch( transcribeArgs.visualState )
	{
	case eVisualState::Running:
		transcribeArgs.visualState = eVisualState::Stopping;
		transcribeButton.EnableWindow( FALSE );
		return;
	case eVisualState::Stopping:
		return;
	}

	// Validate input. A manually typed path remains a one-file job.
	transcribeArgs.mediaFiles = selectedMediaPaths;
	if( transcribeArgs.mediaFiles.empty() )
	{
		CString typedPath;
		sourceMediaPath.GetWindowText( typedPath );
		if( typedPath.GetLength() > 0 )
			transcribeArgs.mediaFiles.emplace_back( typedPath );
	}
	if( transcribeArgs.mediaFiles.empty() )
	{
		transcribeError( L"请选择一个视频或音频文件" );
		return;
	}
	for( const CString& mediaPath : transcribeArgs.mediaFiles )
	{
		if( !PathFileExists( mediaPath ) )
		{
			transcribeError( L"选择的视频或音频文件不存在", HRESULT_FROM_WIN32( ERROR_FILE_NOT_FOUND ) );
			return;
		}
	}
	transcribeArgs.pathMedia = transcribeArgs.mediaFiles.front();

	transcribeArgs.language = languageSelector.selectedLanguage();
	transcribeArgs.translate = cbTranslate.checked();
	if( isInvalidTranslate( m_hWnd, transcribeArgs.language, transcribeArgs.translate ) )
		return;

	transcribeArgs.format = (eOutputFormat)(uint8_t)transcribeOutFormat.GetCurSel();
	transcribeArgs.outputFiles.clear();
	if( transcribeArgs.format != eOutputFormat::None )
	{
		if( transcribeArgs.mediaFiles.size() > 1 )
		{
			if( !isChecked( useInputFolder ) )
			{
				transcribeError( L"批量转写只能输出到每个源文件所在目录。请选择“输出至源文件位置”。" );
				return;
			}
			for( const CString& mediaPath : transcribeArgs.mediaFiles )
			{
				CString outputPath;
				if( !makeBatchOutputPath( outputPath, mediaPath, (int)transcribeArgs.format - 1 ) )
				{
					transcribeError( L"无法生成输出文件名" );
					return;
				}
				transcribeArgs.outputFiles.emplace_back( outputPath );
			}
		}
		else
		{
			transcribeOutputPath.GetWindowText( transcribeArgs.pathOutput );
			if( transcribeArgs.pathOutput.GetLength() <= 0 )
			{
				transcribeError( L"请选择用于输出的文本文档" );
				return;
			}
			transcribeArgs.outputFiles.emplace_back( transcribeArgs.pathOutput );
		}

		size_t existing = 0;
		for( const CString& outputPath : transcribeArgs.outputFiles )
			if( PathFileExists( outputPath ) )
				existing++;
		if( existing > 0 )
		{
			CString question;
			if( transcribeArgs.outputFiles.size() == 1 )
				question = L"输出文件已存在。\n覆盖文件？";
			else
				question.Format( L"有 %zu 个输出文件已存在。\n覆盖这些文件？", existing );
			if( MessageBox( question, L"确认覆盖", MB_ICONQUESTION | MB_YESNO ) != IDYES )
				return;
		}
		transcribeArgs.pathOutput = transcribeArgs.outputFiles.front();
		appState.stringStore( regValOutPath, transcribeArgs.pathOutput );
	}
	else
		cbConsole.ensureChecked();

	appState.dwordStore( regValOutFormat, (uint32_t)(int)transcribeArgs.format );
	appState.boolStore( regValUseInputFolder, isChecked( useInputFolder ) );
	languageSelector.saveSelection( appState );
	cbTranslate.saveSelection( appState );
	appState.stringStore( regValInput, transcribeArgs.pathMedia );

	setPending( true );
	transcribeArgs.visualState = eVisualState::Running;
	transcribeButton.SetWindowText( L"停止" );
	work.post();
}

void __stdcall TranscribeDlg::poolCallback() noexcept
{
	HRESULT hr = transcribe();
	PostMessage( WM_CALLBACK_STATUS, (WPARAM)hr );
}

static void printTime( CString& rdi, int64_t ticks )
{
	const Whisper::sTimeSpan ts{ (uint64_t)ticks };
	const Whisper::sTimeSpanFields fields = ts;

	if( fields.days != 0 )
	{
		rdi.AppendFormat( L"%i 天, %i 小时", fields.days, (int)fields.hours );
		return;
	}
	if( ( fields.hours | fields.minutes ) != 0 )
	{
		rdi.AppendFormat( L"%02d:%02d:%02d", (int)fields.hours, (int)fields.minutes, (int)fields.seconds );
		return;
	}
	rdi.AppendFormat( L"%.3f 秒", (double)ticks / 1E7 );
}

LRESULT TranscribeDlg::onCallbackStatus( UINT, WPARAM wParam, LPARAM, BOOL& bHandled )
{
	setPending( false );
	transcribeButton.SetWindowText( L"转写" );
	transcribeButton.EnableWindow( TRUE );
	const bool prematurely = ( transcribeArgs.visualState == eVisualState::Stopping );
	transcribeArgs.visualState = eVisualState::Idle;

	const HRESULT hr = (HRESULT)wParam;
	if( FAILED( hr ) )
	{
		LPCTSTR failMessage = L"转写失败";

		if( transcribeArgs.errorMessage.GetLength() > 0 )
		{
			CString tmp = failMessage;
			tmp += L"\n";
			tmp += transcribeArgs.errorMessage;
			transcribeError( tmp, hr );
		}
		else
			transcribeError( failMessage, hr );

		return 0;
	}

	const int64_t elapsed = ( GetTickCount64() - transcribeArgs.startTime ) * 10'000;
	const int64_t media = transcribeArgs.mediaDuration;
	CString message;
	if( prematurely )
		message = L"转写已停止，已处理音频的初始部分";
	else if( transcribeArgs.completedFiles > 1 )
		message.Format( L"已完成 %zu 个文件的转写", transcribeArgs.completedFiles );
	else
		message = L"已完成文件转写";
	message += L"\n媒体时长: ";
	printTime( message, media );
	message += L"\n处理时间: ";
	printTime( message, elapsed );
	message += L"\n相对处理速度: ";
	double mul = (double)media / (double)elapsed;
	message.AppendFormat( L"%g", mul );
	if( transcribeArgs.outputFiles.size() == 1 )
	{
		message += L"\n输出文件: ";
		message += transcribeArgs.outputFiles.front();
	}
	else if( transcribeArgs.outputFiles.size() > 1 )
		message += L"\n输出文件已分别保存到各源文件所在目录。";
	if( !transcribeArgs.failedFiles.empty() )
	{
		message.AppendFormat( L"\n已跳过 %zu 个失败文件，其他文件已继续处理。", transcribeArgs.failedFiles.size() );
		message += L"\n失败文件: ";
		for( size_t i = 0; i < transcribeArgs.failedFiles.size() && i < 3; i++ )
		{
			if( i != 0 )
				message += L"；";
			message += PathFindFileName( transcribeArgs.failedFiles[ i ] );
		}
		if( transcribeArgs.failedFiles.size() > 3 )
			message += L"；…";
	}

	MessageBox( message, L"转写完成", MB_OK | MB_ICONINFORMATION );
	return 0;
}

void TranscribeDlg::getThreadError()
{
	getLastError( transcribeArgs.errorMessage );
}

#define CHECK_EX( hr ) { const HRESULT __hr = ( hr ); if( FAILED( __hr ) ) { getThreadError(); return __hr; } }

HRESULT TranscribeDlg::transcribe()
{
	transcribeArgs.startTime = GetTickCount64();
	transcribeArgs.mediaDuration = 0;
	transcribeArgs.completedFiles = 0;
	transcribeArgs.failedFiles.clear();
	transcribeArgs.failureDetails.Empty();
	transcribeArgs.errorMessage.Empty();

	for( size_t i = 0; i < transcribeArgs.mediaFiles.size(); i++ )
	{
		if( transcribeArgs.visualState == eVisualState::Stopping )
			return S_FALSE;
		transcribeArgs.pathMedia = transcribeArgs.mediaFiles[ i ];
		if( i < transcribeArgs.outputFiles.size() )
			transcribeArgs.pathOutput = transcribeArgs.outputFiles[ i ];
		transcribeArgs.errorMessage.Empty();
		HRESULT hr = transcribeOne();
		if( FAILED( hr ) )
		{
			CString reason = transcribeArgs.errorMessage;
			if( reason.IsEmpty() )
				reason = formatErrorMessage( hr );
			transcribeArgs.failedFiles.emplace_back( transcribeArgs.pathMedia );
			transcribeArgs.failureDetails += PathFindFileName( transcribeArgs.pathMedia );
			transcribeArgs.failureDetails += L": ";
			transcribeArgs.failureDetails += reason;
			transcribeArgs.failureDetails += L"\n";
			if( transcribeArgs.format != eOutputFormat::None )
				DeleteFile( transcribeArgs.pathOutput );
			continue;
		}
		transcribeArgs.completedFiles++;
	}

	if( transcribeArgs.completedFiles == 0 && !transcribeArgs.failedFiles.empty() )
	{
		transcribeArgs.errorMessage = transcribeArgs.failureDetails;
		return E_FAIL;
	}
	return S_OK;
}

HRESULT TranscribeDlg::transcribeOne()
{
	clearLastError();

	using namespace Whisper;
	// Declare the temporary before the reader: C++ then releases the reader first,
	// so the temporary WAV is no longer held open when its destructor deletes it.
	sTemporaryWav temporaryWav;
	CComPtr<iAudioReader> reader;

	HRESULT openHr = appState.mediaFoundation->openAudioFile( transcribeArgs.pathMedia, false, &reader );
	if( FAILED( openHr ) )
	{
		CString extension = PathFindExtension( transcribeArgs.pathMedia );
		if( 0 != extension.CompareNoCase( L".flv" ) )
		{
			getThreadError();
			return openHr;
		}

		// Media Foundation cannot normally decode FLV. Convert it to a temporary WAV
		// through the bundled sidecar ffmpeg.exe, then use the existing audio pipeline.
		clearLastError();
		HRESULT convertHr = convertFlvToWav( transcribeArgs.pathMedia, temporaryWav.path, transcribeArgs.errorMessage );
		if( FAILED( convertHr ) )
			return convertHr;
		openHr = appState.mediaFoundation->openAudioFile( temporaryWav.path, false, &reader );
		if( FAILED( openHr ) )
		{
			CString mediaError;
			getLastError( mediaError );
			transcribeArgs.errorMessage = L"FFmpeg 已转换 FLV，但 Windows 无法打开临时 WAV。";
			if( !mediaError.IsEmpty() )
			{
				transcribeArgs.errorMessage += L"\n";
				transcribeArgs.errorMessage += mediaError;
			}
			return openHr;
		}
	}

	const eOutputFormat format = transcribeArgs.format;
	CAtlFile outputFile;
	if( format != eOutputFormat::None )
		CHECK( outputFile.Create( transcribeArgs.pathOutput, GENERIC_WRITE, 0, CREATE_ALWAYS ) );

	transcribeArgs.resultFlags = eResultFlags::Timestamps | eResultFlags::Tokens;

	CComPtr<iContext> context;
	CHECK_EX( appState.model->createContext( &context ) );

	sFullParams fullParams;
	CHECK_EX( context->fullDefaultParams( eSamplingStrategy::Greedy, &fullParams ) );
	fullParams.language = transcribeArgs.language;
	fullParams.setFlag( eFullParamsFlags::Translate, transcribeArgs.translate );
	fullParams.resetFlag( eFullParamsFlags::PrintRealtime );

	// Setup the callbacks
	fullParams.new_segment_callback = &newSegmentCallbackStatic;
	fullParams.new_segment_callback_user_data = this;
	fullParams.encoder_begin_callback = &encoderBeginCallback;
	fullParams.encoder_begin_callback_user_data = this;

	// Setup the progress indication sink
	sProgressSink progressSink{ &progressCallbackStatic, this };
	// Run the transcribe
	CHECK_EX( context->runStreamed( fullParams, progressSink, reader ) );

	// Once finished, query duration of the audio.
	// The duration before the processing is sometimes different, by 20 seconds for the file in that issue:
	// https://github.com/Const-me/Whisper/issues/4
	int64_t oneMediaDuration = 0;
	CHECK_EX( reader->getDuration( oneMediaDuration ) );
	transcribeArgs.mediaDuration += oneMediaDuration;

	context->timingsPrint();

	if( format == eOutputFormat::None )
		return S_OK;

	CComPtr<iTranscribeResult> result;
	CHECK_EX( context->getResults( transcribeArgs.resultFlags, &result ) );

	sTranscribeLength len;
	CHECK_EX( result->getSize( len ) );
	const sSegment* const segments = result->getSegments();

	switch( format )
	{
	case eOutputFormat::Text:
		return writeTextFile( segments, len.countSegments, outputFile, false );
	case eOutputFormat::TextTimestamps:
		return writeTextFile( segments, len.countSegments, outputFile, true );
	case eOutputFormat::SubRip:
		return writeSubRip( segments, len.countSegments, outputFile );
	case eOutputFormat::WebVTT:
		return writeWebVTT( segments, len.countSegments, outputFile );
	default:
		return E_FAIL;
	}
}

#undef CHECK_EX

inline HRESULT TranscribeDlg::progressCallback( double p ) noexcept
{
	constexpr double mul = progressMaxInteger;
	int pos = lround( mul * p );
	progressBar.PostMessage( PBM_SETPOS, pos, 0 );
	return S_OK;
}

HRESULT __cdecl TranscribeDlg::progressCallbackStatic( double p, Whisper::iContext* ctx, void* pv ) noexcept
{
	TranscribeDlg* dlg = (TranscribeDlg*)pv;
	return dlg->progressCallback( p );
}

namespace
{
	HRESULT write( CAtlFile& file, const CStringA& line )
	{
		if( line.GetLength() > 0 )
			CHECK( file.Write( cstr( line ), (DWORD)line.GetLength() ) );
		return S_OK;
	}

	const char* skipBlank( const char* rsi )
	{
		while( true )
		{
			const char c = *rsi;
			if( c == ' ' || c == '\t' )
			{
				rsi++;
				continue;
			}
			return rsi;
		}
	}
}

using Whisper::sSegment;


HRESULT TranscribeDlg::writeTextFile( const sSegment* const segments, const size_t length, CAtlFile& file, bool timestamps )
{
	using namespace Whisper;
	CHECK( writeUtf8Bom( file ) );
	CStringA line;
	for( size_t i = 0; i < length; i++ )
	{
		const sSegment& seg = segments[ i ];

		if( timestamps )
		{
			line = "[";
			printTime( line, seg.time.begin );
			line += " --> ";
			printTime( line, seg.time.end );
			line += "]  ";
		}
		else
			line = "";

		line += skipBlank( seg.text );
		line += "\r\n";
		CHECK( write( file, line ) );
	}
	return S_OK;
}

HRESULT TranscribeDlg::writeSubRip( const sSegment* const segments, const size_t length, CAtlFile& file )
{
	CHECK( writeUtf8Bom( file ) );
	CStringA line;
	for( size_t i = 0; i < length; i++ )
	{
		const sSegment& seg = segments[ i ];

		line.Format( "%zu\r\n", i + 1 );
		printTime( line, seg.time.begin, true );
		line += " --> ";
		printTime( line, seg.time.end, true );
		line += "\r\n";
		line += skipBlank( seg.text );
		line += "\r\n\r\n";
		CHECK( write( file, line ) );
	}
	return S_OK;
}

HRESULT TranscribeDlg::writeWebVTT( const sSegment* const segments, const size_t length, CAtlFile& file )
{
	CHECK( writeUtf8Bom( file ) );
	CStringA line;
	line = "WEBVTT\r\n\r\n";
	CHECK( write( file, line ) );

	for( size_t i = 0; i < length; i++ )
	{
		const sSegment& seg = segments[ i ];
		line = "";

		printTime( line, seg.time.begin, false );
		line += " --> ";
		printTime( line, seg.time.end, false );
		line += "\r\n";
		line += skipBlank( seg.text );
		line += "\r\n\r\n";
		CHECK( write( file, line ) );
	}
	return S_OK;
}

inline HRESULT TranscribeDlg::newSegmentCallback( Whisper::iContext* ctx, uint32_t n_new )
{
	using namespace Whisper;
	CComPtr<iTranscribeResult> result;
	CHECK( ctx->getResults( transcribeArgs.resultFlags, &result ) );
	return logNewSegments( result, n_new );
}

HRESULT __cdecl TranscribeDlg::newSegmentCallbackStatic( Whisper::iContext* ctx, uint32_t n_new, void* user_data ) noexcept
{
	TranscribeDlg* dlg = (TranscribeDlg*)user_data;
	return dlg->newSegmentCallback( ctx, n_new );
}

HRESULT __cdecl TranscribeDlg::encoderBeginCallback( Whisper::iContext* ctx, void* user_data ) noexcept
{
	TranscribeDlg* dlg = (TranscribeDlg*)user_data;
	const eVisualState visualState = dlg->transcribeArgs.visualState;
	switch( visualState )
	{
	case eVisualState::Idle:
		return E_NOT_VALID_STATE;
	case eVisualState::Running:
		return S_OK;
	case eVisualState::Stopping:
		return S_FALSE;
	default:
		return E_UNEXPECTED;
	}
}

void TranscribeDlg::onWmClose()
{
	if( GetDlgItem( IDCANCEL ).IsWindowEnabled() )
	{
		EndDialog( IDCANCEL );
		return;
	}

	constexpr UINT flags = MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2;
	const int res = this->MessageBox( L"Transcribe is in progress.\nDo you want to quit anyway?", L"Confirm exit", flags );
	if( res != IDYES )
		return;

	// TODO: instead of ExitProcess(), implement another callback in the DLL API, for proper cancellation of the background task
	ExitProcess( 1 );
}