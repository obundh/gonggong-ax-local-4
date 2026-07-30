using System.IO;

namespace Series4.Desktop;

public sealed record MacroProjectSummary(
    string ProjectPath,
    string CurrentVideoPath,
    DateTimeOffset RecordedAtUtc,
    DateTimeOffset SavedAtUtc,
    MacroProjectStatus Status,
    int EventCount,
    int ExecutableCount,
    bool VideoExists,
    bool IsReadable,
    string? ErrorMessage
)
{
    public string DateGroup =>
        RecordedAtUtc.ToLocalTime().ToString("yyyy년 M월 d일");

    public string RecordedAtText =>
        RecordedAtUtc.ToLocalTime().ToString("HH:mm:ss");

    public string SavedAtText =>
        $"마지막 저장 {SavedAtUtc.ToLocalTime():MM-dd HH:mm}";

    public string StatusText => !IsReadable
        ? "읽기 실패"
        : Status switch
        {
            MacroProjectStatus.Completed => "완료",
            MacroProjectStatus.InProgress => "녹화 중단",
            _ => "검토 필요",
        };

    public string EventCountText => $"{EventCount:N0}개 로그";

    public string ExecutableCountText => $"{ExecutableCount:N0}개 실행";

    public string VideoStatusText => VideoExists ? "영상 있음" : "영상 없음";

    public string DisplayName =>
        string.IsNullOrWhiteSpace(CurrentVideoPath)
            ? Path.GetFileName(ProjectPath)
            : Path.GetFileNameWithoutExtension(CurrentVideoPath);

    public string FolderPath =>
        Path.GetDirectoryName(ProjectPath) ?? ProjectPath;

    public bool IsLoadable => IsReadable;
}
