using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AsrUi;

internal sealed record SubtitleTimelineRepairResult(
    int InputCues,
    int OutputCues,
    int DuplicateZeroLengthRemoved,
    int ZeroLengthMerged,
    int ZeroLengthExpanded,
    int SmallOverlapsFixed,
    int MajorOverlapsLeftUntouched,
    int ObviousLongCuesShortened)
{
    public bool Changed => DuplicateZeroLengthRemoved + ZeroLengthMerged + ZeroLengthExpanded + SmallOverlapsFixed + ObviousLongCuesShortened > 0;

    public string Summary => $"时间轴清理：输入 {InputCues} 条，输出 {OutputCues} 条；"
        + $"零时长去重 {DuplicateZeroLengthRemoved}，合并 {ZeroLengthMerged}，补时 {ZeroLengthExpanded}；"
        + $"小重叠修复 {SmallOverlapsFixed}，明显超长收缩 {ObviousLongCuesShortened}"
        + (MajorOverlapsLeftUntouched > 0 ? $"；保留 {MajorOverlapsLeftUntouched} 个大重叠以避免错误移动字幕。" : "。");
}

internal static class SubtitleTimelineRepair
{
    private static readonly Regex TimeLinePattern = new(@"^(?<start>\d{2}:\d{2}:\d{2},\d{3})\s*-->\s*(?<end>\d{2}:\d{2}:\d{2},\d{3})\s*$", RegexOptions.Compiled);
    private static readonly Regex VisibleCharacterPattern = new(@"[\p{L}\p{N}]", RegexOptions.Compiled);
    private static readonly Regex ComparableTextPattern = new(@"[\s\p{P}\p{S}]", RegexOptions.Compiled);
    private static readonly TimeSpan MergeWindow = TimeSpan.FromMilliseconds(420);
    private static readonly TimeSpan MinimumCueLength = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan SmallOverlapWindow = TimeSpan.FromMilliseconds(450);

    public static SubtitleTimelineRepairResult RepairFile(string srtPath)
    {
        var original = File.ReadAllText(srtPath);
        var cues = Parse(original);
        if (cues.Count == 0)
            return new SubtitleTimelineRepairResult(0, 0, 0, 0, 0, 0, 0, 0);

        var duplicateZeroLengthRemoved = 0;
        var zeroLengthMerged = 0;
        var zeroLengthExpanded = 0;
        var smallOverlapsFixed = 0;
        var majorOverlapsLeftUntouched = 0;
        var obviousLongCuesShortened = 0;
        var repaired = new List<SubtitleCue>(cues.Count);

        for (var index = 0; index < cues.Count; index++)
        {
            var cue = cues[index];
            if (cue.End > cue.Start)
            {
                repaired.Add(cue);
                continue;
            }

            var previous = repaired.Count > 0 ? repaired[^1] : null;
            var next = FindNextValidCue(cues, index + 1);
            if (previous is not null
                && IsSameText(previous.Text, cue.Text)
                && cue.Start - previous.End <= TimeSpan.FromSeconds(1))
            {
                duplicateZeroLengthRemoved++;
                continue;
            }

            if (previous is not null && cue.Start >= previous.Start && cue.Start - previous.End <= MergeWindow)
            {
                previous.Text = JoinText(previous.Text, cue.Text);
                zeroLengthMerged++;
                continue;
            }

            if (next is not null && next.Start >= cue.Start && next.Start - cue.Start <= MergeWindow)
            {
                next.Text = JoinText(cue.Text, next.Text);
                next.Start = cue.Start;
                zeroLengthMerged++;
                continue;
            }

            cue.Start = previous is not null && previous.End > cue.Start ? previous.End : cue.Start;
            var maximumEnd = next?.Start ?? cue.Start + MinimumCueLength;
            cue.End = Math.Min(cue.Start + MinimumCueLength, maximumEnd);
            if (cue.End <= cue.Start)
            {
                cue.End = cue.Start + TimeSpan.FromMilliseconds(120);
            }
            repaired.Add(cue);
            zeroLengthExpanded++;
        }

        for (var index = 1; index < repaired.Count; index++)
        {
            var previous = repaired[index - 1];
            var current = repaired[index];
            if (current.Start >= previous.End) continue;

            var overlap = previous.End - current.Start;
            var boundary = previous.End - TimeSpan.FromTicks(overlap.Ticks / 2);
            if (overlap <= SmallOverlapWindow
                && boundary - previous.Start >= MinimumCueLength
                && current.End - boundary >= MinimumCueLength)
            {
                previous.End = boundary;
                current.Start = boundary;
                smallOverlapsFixed++;
            }
            else
            {
                majorOverlapsLeftUntouched++;
            }
        }

        for (var index = 0; index < repaired.Count; index++)
        {
            var cue = repaired[index];
            var visibleCharacters = VisibleCharacterPattern.Matches(cue.Text).Count;
            var seconds = cue.Duration.TotalSeconds;
            var expectedSeconds = Math.Clamp(visibleCharacters * 0.45d, 1.10d, 2.80d);
            // Never shrink ordinary spoken sentences based on text length alone. This rule is deliberately
            // limited to a very short utterance held for an implausibly long span, such as a 4-character
            // reaction stretched to ten seconds by a failed alignment.
            if (visibleCharacters == 0 || visibleCharacters > 7 || seconds < 5 || seconds <= expectedSeconds * 2.5d) continue;

            var nextStart = index + 1 < repaired.Count ? repaired[index + 1].Start : TimeSpan.MaxValue;
            var candidateEnd = cue.Start + TimeSpan.FromSeconds(expectedSeconds);
            if (nextStart != TimeSpan.MaxValue)
                candidateEnd = TimeSpan.FromTicks(Math.Min(candidateEnd.Ticks, Math.Max(cue.Start.Ticks + MinimumCueLength.Ticks, nextStart.Ticks - TimeSpan.FromMilliseconds(50).Ticks)));

            if (candidateEnd < cue.End && candidateEnd > cue.Start + TimeSpan.FromMilliseconds(200))
            {
                cue.End = candidateEnd;
                obviousLongCuesShortened++;
            }
        }

        Write(srtPath, repaired);
        return new SubtitleTimelineRepairResult(
            cues.Count,
            repaired.Count,
            duplicateZeroLengthRemoved,
            zeroLengthMerged,
            zeroLengthExpanded,
            smallOverlapsFixed,
            majorOverlapsLeftUntouched,
            obviousLongCuesShortened);
    }

    private static List<SubtitleCue> Parse(string input)
    {
        var normalized = input.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var cues = new List<SubtitleCue>();
        foreach (var block in Regex.Split(normalized, "\n{2,}"))
        {
            var lines = block.Split('\n');
            if (lines.Length < 3) continue;
            var timeLine = TimeLinePattern.Match(lines[1].Trim());
            if (!timeLine.Success) continue;
            if (!TryParseSrtTime(timeLine.Groups["start"].Value, out var start)
                || !TryParseSrtTime(timeLine.Groups["end"].Value, out var end)) continue;
            var text = string.Join(Environment.NewLine, lines.Skip(2)).Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            cues.Add(new SubtitleCue(start, end, text));
        }
        return cues;
    }

    private static SubtitleCue? FindNextValidCue(IReadOnlyList<SubtitleCue> cues, int startIndex)
    {
        for (var index = startIndex; index < cues.Count; index++)
        {
            if (cues[index].End > cues[index].Start) return cues[index];
        }
        return null;
    }

    private static bool TryParseSrtTime(string value, out TimeSpan result)
    {
        return TimeSpan.TryParseExact(value, @"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture, out result);
    }

    private static string FormatSrtTime(TimeSpan value)
    {
        return value.ToString(@"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture);
    }

    private static string JoinText(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left)) return right;
        if (string.IsNullOrWhiteSpace(right)) return left;
        return left.TrimEnd() + Environment.NewLine + right.TrimStart();
    }

    private static bool IsSameText(string left, string right)
    {
        return ComparableTextPattern.Replace(left, string.Empty)
            .Equals(ComparableTextPattern.Replace(right, string.Empty), StringComparison.Ordinal);
    }

    private static void Write(string path, IReadOnlyList<SubtitleCue> cues)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < cues.Count; index++)
        {
            var cue = cues[index];
            builder.Append(index + 1).AppendLine();
            builder.Append(FormatSrtTime(cue.Start)).Append(" --> ").Append(FormatSrtTime(cue.End)).AppendLine();
            builder.AppendLine(cue.Text.Trim());
            builder.AppendLine();
        }
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private sealed class SubtitleCue
    {
        public SubtitleCue(TimeSpan start, TimeSpan end, string text)
        {
            Start = start;
            End = end;
            Text = text;
        }

        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public string Text { get; set; }
        public TimeSpan Duration => End - Start;
    }
}
