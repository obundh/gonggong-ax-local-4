using System.Reflection;
using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SharpHook.Data;

namespace Series4.Desktop;

public enum MacroProjectStatus
{
    Completed,
    InProgress,
    Failed,
}

public static class MacroProjectStore
{
    public const int CurrentVersion = 1;
    public const string SidecarSuffix = ".series4.json";

    private const string ApplicationFolderName = "공공AX 업무 매크로";
    private const string LastProjectFileName = "last-project.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string LastProjectStatePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationFolderName,
            LastProjectFileName
        );

    public static string ProjectRootPath
    {
        get
        {
            var videosFolder = Environment.GetFolderPath(
                Environment.SpecialFolder.MyVideos
            );
            return string.IsNullOrWhiteSpace(videosFolder)
                ? Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData
                    ),
                    ApplicationFolderName
                )
                : Path.Combine(videosFolder, ApplicationFolderName);
        }
    }

    public static string ProjectLibraryPath =>
        Path.Combine(ProjectRootPath, "기록 저장소");

    public static string GetDatedProjectDirectory(DateTimeOffset recordedAt)
    {
        var local = recordedAt.ToLocalTime();
        return Path.Combine(
            ProjectLibraryPath,
            local.ToString("yyyy"),
            local.ToString("yyyy-MM"),
            local.ToString("yyyy-MM-dd")
        );
    }

    public static string GetSidecarPath(string currentVideoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVideoPath);
        return Path.GetFullPath(currentVideoPath) + SidecarSuffix;
    }

    public static async Task<string> SaveAsync(
        string currentVideoPath,
        IEnumerable<RecordedEvent> recordedEvents,
        MacroProjectStatus status = MacroProjectStatus.Completed,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVideoPath);
        ArgumentNullException.ThrowIfNull(recordedEvents);

        var normalizedVideoPath = Path.GetFullPath(currentVideoPath);
        var sidecarPath = GetSidecarPath(normalizedVideoPath);
        await SaveToPathAsync(
            sidecarPath,
            normalizedVideoPath,
            recordedEvents,
            status,
            cancellationToken
        );
        return sidecarPath;
    }

    public static async Task SaveToPathAsync(
        string sidecarPath,
        string currentVideoPath,
        IEnumerable<RecordedEvent> recordedEvents,
        MacroProjectStatus status = MacroProjectStatus.Completed,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sidecarPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVideoPath);
        ArgumentNullException.ThrowIfNull(recordedEvents);

        var normalizedSidecarPath = Path.GetFullPath(sidecarPath);
        var normalizedVideoPath = Path.GetFullPath(currentVideoPath);
        var sidecarDirectory = Path.GetDirectoryName(normalizedSidecarPath)
            ?? throw new InvalidOperationException(
                $"프로젝트 폴더를 확인할 수 없습니다: {normalizedSidecarPath}"
            );
        var document = new MacroProjectDocumentDto
        {
            Version = CurrentVersion,
            CurrentVideoPath = Path.GetRelativePath(
                sidecarDirectory,
                normalizedVideoPath
            ),
            SavedAtUtc = DateTimeOffset.UtcNow,
            Status = status.ToString(),
            Events = recordedEvents.Select(ToDto).ToList(),
        };

        await WriteJsonAtomicallyAsync(
            normalizedSidecarPath,
            document,
            cancellationToken
        );
        await RememberLastProjectPathAsync(
            normalizedSidecarPath,
            cancellationToken
        );
    }

    public static async Task<LoadedMacroProject> LoadAsync(
        string sidecarPath,
        CancellationToken cancellationToken = default
    )
    {
        var project = await ReadAsync(sidecarPath, cancellationToken);
        await RememberLastProjectPathAsync(
            project.ProjectPath,
            cancellationToken
        );
        return project;
    }

    public static async Task<LoadedMacroProject> ReadAsync(
        string sidecarPath,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sidecarPath);
        var normalizedSidecarPath = Path.GetFullPath(sidecarPath);
        var document = await ReadJsonAsync<MacroProjectDocumentDto>(
            normalizedSidecarPath,
            cancellationToken
        );
        ValidateDocument(document);
        var currentVideoPath = ResolveVideoPath(
            normalizedSidecarPath,
            document.CurrentVideoPath!
        );

        var events = document.Events!.Select(FromDto).ToList();
        var status = ParseStatus(document.Status);

        return new LoadedMacroProject(
            normalizedSidecarPath,
            currentVideoPath,
            events,
            status
        );
    }

    public static async Task<IReadOnlyList<MacroProjectSummary>> ListProjectsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var items = new List<MacroProjectSummary>();
        var projectPaths = new List<string>();
        var knownProjectPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );
        try
        {
            Directory.CreateDirectory(ProjectLibraryPath);
            foreach (
                var projectPath in Directory.EnumerateFiles(
                    ProjectRootPath,
                    $"*{SidecarSuffix}",
                    SearchOption.AllDirectories
                )
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalizedProjectPath = Path.GetFullPath(projectPath);
                if (knownProjectPaths.Add(normalizedProjectPath))
                {
                    projectPaths.Add(normalizedProjectPath);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException
        )
        {
            throw new IOException(
                $"기록 저장소를 읽지 못했습니다: {ProjectRootPath}",
                exception
            );
        }

        foreach (var projectPath in projectPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var document = await ReadJsonAsync<MacroProjectDocumentDto>(
                    projectPath,
                    cancellationToken
                );
                ValidateDocument(document);
                var videoPath = ResolveVideoPath(
                    projectPath,
                    document.CurrentVideoPath!
                );
                var savedAtUtc = document.SavedAtUtc == default
                    ? GetLastWriteTimeUtcOrDefault(
                        projectPath,
                        DateTimeOffset.UnixEpoch
                    )
                    : document.SavedAtUtc.ToUniversalTime();
                var recordedAtUtc = GetRecordedAtUtc(
                    projectPath,
                    savedAtUtc
                );
                var events = document.Events!;
                items.Add(
                    new MacroProjectSummary(
                        projectPath,
                        videoPath,
                        recordedAtUtc,
                        savedAtUtc,
                        ParseStatus(document.Status),
                        events.Count,
                        events.Count(
                            item =>
                                !item.IsQuarantined
                                && !string.Equals(
                                    item.ActionKind,
                                    MacroActionKind.None.ToString(),
                                    StringComparison.Ordinal
                                )
                        ),
                        File.Exists(videoPath),
                        true,
                        null
                    )
                );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or JsonException
                    or InvalidDataException
                    or NotSupportedException
                    or ArgumentException
                    or System.Security.SecurityException
            )
            {
                var savedAtUtc = GetLastWriteTimeUtcOrDefault(
                    projectPath,
                    DateTimeOffset.UnixEpoch
                );
                items.Add(
                    new MacroProjectSummary(
                        projectPath,
                        string.Empty,
                        GetRecordedAtUtc(
                            projectPath,
                            savedAtUtc
                        ),
                        savedAtUtc,
                        MacroProjectStatus.Failed,
                        0,
                        0,
                        false,
                        false,
                        exception.Message
                    )
                );
            }
        }

        return items
            .OrderByDescending(item => item.RecordedAtUtc)
            .ThenByDescending(item => item.ProjectPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static async Task<string?> GetLastProjectPathAsync(
        CancellationToken cancellationToken = default
    )
    {
        var statePath = LastProjectStatePath;
        if (!File.Exists(statePath))
        {
            return null;
        }

        var state = await ReadJsonAsync<LastProjectStateDto>(
            statePath,
            cancellationToken
        );
        if (state.Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"지원하지 않는 최근 프로젝트 상태 버전입니다: {state.Version}"
            );
        }

        if (string.IsNullOrWhiteSpace(state.ProjectPath))
        {
            throw new InvalidDataException(
                "최근 프로젝트 상태에 프로젝트 경로가 없습니다."
            );
        }

        return Path.GetFullPath(state.ProjectPath);
    }

    public static async Task<LoadedMacroProject?> LoadLastProjectAsync(
        CancellationToken cancellationToken = default
    )
    {
        var projectPath = await GetLastProjectPathAsync(cancellationToken);
        return projectPath is null
            ? null
            : await LoadAsync(projectPath, cancellationToken);
    }

    public static async Task RememberLastProjectPathAsync(
        string sidecarPath,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sidecarPath);
        var state = new LastProjectStateDto
        {
            Version = CurrentVersion,
            ProjectPath = Path.GetFullPath(sidecarPath),
        };
        await WriteJsonAtomicallyAsync(
            LastProjectStatePath,
            state,
            cancellationToken
        );
    }

    private static void ValidateDocument(MacroProjectDocumentDto document)
    {
        if (document.Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"지원하지 않는 매크로 프로젝트 버전입니다: {document.Version}"
            );
        }

        if (string.IsNullOrWhiteSpace(document.CurrentVideoPath))
        {
            throw new InvalidDataException(
                "매크로 프로젝트에 영상 경로가 없습니다."
            );
        }

        if (document.Events is null)
        {
            throw new InvalidDataException(
                "매크로 프로젝트에 이벤트 목록이 없습니다."
            );
        }
    }

    private static string ResolveVideoPath(
        string normalizedSidecarPath,
        string storedVideoPath
    )
    {
        var sidecarDirectory = Path.GetDirectoryName(normalizedSidecarPath)
            ?? throw new InvalidDataException(
                $"프로젝트 폴더를 확인할 수 없습니다: {normalizedSidecarPath}"
            );
        var currentVideoPath = Path.IsPathRooted(storedVideoPath)
            ? Path.GetFullPath(storedVideoPath)
            : Path.GetFullPath(
                Path.Combine(sidecarDirectory, storedVideoPath)
            );
        if (!File.Exists(currentVideoPath))
        {
            var portableFallback = Path.Combine(
                sidecarDirectory,
                Path.GetFileName(storedVideoPath)
            );
            if (File.Exists(portableFallback))
            {
                currentVideoPath = portableFallback;
            }
        }

        return currentVideoPath;
    }

    private static MacroProjectStatus ParseStatus(string? storedStatus)
    {
        var statusName = Enum.GetNames<MacroProjectStatus>()
            .FirstOrDefault(
                name =>
                    string.Equals(
                        name,
                        storedStatus?.Trim(),
                        StringComparison.OrdinalIgnoreCase
                    )
            );
        return statusName is not null
            ? Enum.Parse<MacroProjectStatus>(statusName)
            : MacroProjectStatus.Failed;
    }

    private static DateTimeOffset GetRecordedAtUtc(
        string projectPath,
        DateTimeOffset savedAtUtc
    )
    {
        var fileName = Path.GetFileName(projectPath);
        var match = Regex.Match(
            fileName,
            @"^업무시연_(?<stamp>\d{8}_\d{6}(?:_\d{3})?)(?:_[^.]+)?\.mp4\.series4\.json$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
        if (match.Success)
        {
            var stamp = match.Groups["stamp"].Value;
            var formats = stamp.Count(character => character == '_') == 2
                ? new[] { "yyyyMMdd_HHmmss_fff" }
                : new[] { "yyyyMMdd_HHmmss" };
            if (
                DateTime.TryParseExact(
                    stamp,
                    formats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var localDateTime
                )
            )
            {
                localDateTime = DateTime.SpecifyKind(
                    localDateTime,
                    DateTimeKind.Unspecified
                );
                return new DateTimeOffset(
                    localDateTime,
                    TimeZoneInfo.Local.GetUtcOffset(localDateTime)
                ).ToUniversalTime();
            }
        }

        try
        {
            var createdAtUtc = File.GetCreationTimeUtc(projectPath);
            if (createdAtUtc > DateTime.UnixEpoch)
            {
                return new DateTimeOffset(createdAtUtc, TimeSpan.Zero);
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or System.Security.SecurityException
        )
        {
            // The file may disappear or become inaccessible after enumeration.
        }

        return savedAtUtc.ToUniversalTime();
    }

    private static DateTimeOffset GetLastWriteTimeUtcOrDefault(
        string path,
        DateTimeOffset fallback
    )
    {
        try
        {
            var lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
            return lastWriteTimeUtc > DateTime.UnixEpoch
                ? new DateTimeOffset(lastWriteTimeUtc, TimeSpan.Zero)
                : fallback.ToUniversalTime();
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or System.Security.SecurityException
        )
        {
            return fallback.ToUniversalTime();
        }
    }

    private static RecordedEventDto ToDto(RecordedEvent recordedEvent)
    {
        ArgumentNullException.ThrowIfNull(recordedEvent);
        return new RecordedEventDto
        {
            OffsetTicks = recordedEvent.Offset.Ticks,
            Category = recordedEvent.Category,
            Message = recordedEvent.Message,
            OverlayLabel = recordedEvent.OverlayLabel,
            ActionKind = recordedEvent.ActionKind.ToString(),
            ActionText = recordedEvent.ActionText,
            KeyCodes = SerializeKeyCodes(recordedEvent.KeyCodes),
            ModifierKeyCodes = SerializeKeyCodes(
                GetOptionalProperty(
                    recordedEvent,
                    nameof(RecordedEventDto.ModifierKeyCodes),
                    Array.Empty<KeyCode>()
                )
            ),
            WheelRotation = recordedEvent.WheelRotation,
            IsHorizontalWheel = GetOptionalProperty(
                recordedEvent,
                nameof(RecordedEventDto.IsHorizontalWheel),
                false
            ),
            IsQuarantined = GetOptionalProperty(
                recordedEvent,
                nameof(RecordedEventDto.IsQuarantined),
                false
            ),
            QuarantineReason = GetOptionalProperty<string?>(
                recordedEvent,
                nameof(RecordedEventDto.QuarantineReason),
                null
            ),
            Sequence = recordedEvent.Sequence,
            ScreenX = recordedEvent.ScreenX,
            ScreenY = recordedEvent.ScreenY,
            CaptureLeft = recordedEvent.CaptureLeft,
            CaptureTop = recordedEvent.CaptureTop,
            CaptureWidth = recordedEvent.CaptureWidth,
            CaptureHeight = recordedEvent.CaptureHeight,
        };
    }

    private static RecordedEvent FromDto(RecordedEventDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.OffsetTicks < 0)
        {
            throw new InvalidDataException(
                $"이벤트 시점은 0 이상이어야 합니다: {dto.OffsetTicks}"
            );
        }

        if (
            string.IsNullOrWhiteSpace(dto.ActionKind)
            || !Enum.TryParse<MacroActionKind>(
                dto.ActionKind,
                ignoreCase: false,
                out var actionKind
            )
            || !Enum.IsDefined(actionKind)
            || !string.Equals(
                Enum.GetName(actionKind),
                dto.ActionKind,
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidDataException(
                $"알 수 없는 매크로 동작 종류입니다: {dto.ActionKind}"
            );
        }

        var recordedEvent = new RecordedEvent
        {
            Offset = TimeSpan.FromTicks(dto.OffsetTicks),
            Category = dto.Category
                ?? throw new InvalidDataException("이벤트 분류가 없습니다."),
            Message = dto.Message
                ?? throw new InvalidDataException("이벤트 메시지가 없습니다."),
            OverlayLabel = dto.OverlayLabel,
            ActionKind = actionKind,
            ActionText = dto.ActionText,
            KeyCodes = ParseKeyCodes(dto.KeyCodes, nameof(dto.KeyCodes)),
            WheelRotation = dto.WheelRotation,
            Sequence = dto.Sequence,
            ScreenX = dto.ScreenX,
            ScreenY = dto.ScreenY,
            CaptureLeft = dto.CaptureLeft,
            CaptureTop = dto.CaptureTop,
            CaptureWidth = dto.CaptureWidth,
            CaptureHeight = dto.CaptureHeight,
        };

        SetOptionalProperty(
            recordedEvent,
            nameof(RecordedEventDto.ModifierKeyCodes),
            ParseKeyCodes(
                dto.ModifierKeyCodes,
                nameof(dto.ModifierKeyCodes)
            )
        );
        SetOptionalProperty(
            recordedEvent,
            nameof(RecordedEventDto.IsHorizontalWheel),
            dto.IsHorizontalWheel
        );
        SetOptionalProperty(
            recordedEvent,
            nameof(RecordedEventDto.IsQuarantined),
            dto.IsQuarantined
        );
        SetOptionalProperty(
            recordedEvent,
            nameof(RecordedEventDto.QuarantineReason),
            dto.QuarantineReason
        );
        return recordedEvent;
    }

    private static List<string> SerializeKeyCodes(
        IEnumerable<KeyCode>? keyCodes
    )
    {
        return keyCodes?.Select(keyCode => keyCode.ToString()).ToList() ?? [];
    }

    private static KeyCode[] ParseKeyCodes(
        IReadOnlyList<string>? names,
        string fieldName
    )
    {
        if (names is null)
        {
            return [];
        }

        var keyCodes = new KeyCode[names.Count];
        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];
            if (
                string.IsNullOrWhiteSpace(name)
                || !Enum.TryParse<KeyCode>(
                    name,
                    ignoreCase: false,
                    out var keyCode
                )
                || !Enum.IsDefined(keyCode)
                || !string.Equals(
                    Enum.GetName(keyCode),
                    name,
                    StringComparison.Ordinal
                )
            )
            {
                throw new InvalidDataException(
                    $"{fieldName}[{index}]에 알 수 없는 키 코드가 있습니다: {name}"
                );
            }

            keyCodes[index] = keyCode;
        }

        return keyCodes;
    }

    private static T GetOptionalProperty<T>(
        RecordedEvent recordedEvent,
        string propertyName,
        T defaultValue
    )
    {
        var property = typeof(RecordedEvent).GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public
        );
        return property?.CanRead == true
            && property.GetValue(recordedEvent) is T value
            ? value
            : defaultValue;
    }

    private static void SetOptionalProperty<T>(
        RecordedEvent recordedEvent,
        string propertyName,
        T value
    )
    {
        var property = typeof(RecordedEvent).GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public
        );
        if (property?.CanWrite == true)
        {
            property.SetValue(recordedEvent, value);
        }
    }

    private static async Task<T> ReadJsonAsync<T>(
        string path,
        CancellationToken cancellationToken
    )
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        var value = await JsonSerializer.DeserializeAsync<T>(
            stream,
            SerializerOptions,
            cancellationToken
        );
        return value
            ?? throw new InvalidDataException(
                $"JSON 파일의 내용이 비어 있습니다: {path}"
            );
    }

    private static async Task WriteJsonAtomicallyAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken
    )
    {
        var normalizedPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(normalizedPath)
            ?? throw new InvalidOperationException(
                $"저장 폴더를 확인할 수 없습니다: {normalizedPath}"
            );
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(normalizedPath)}.{Guid.NewGuid():N}.tmp"
        );
        try
        {
            await using (
                var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.WriteThrough
                )
            )
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    value,
                    SerializerOptions,
                    cancellationToken
                );
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, normalizedPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

public sealed record LoadedMacroProject(
    string ProjectPath,
    string CurrentVideoPath,
    IReadOnlyList<RecordedEvent> RecordedEvents,
    MacroProjectStatus Status
);

public sealed class MacroProjectDocumentDto
{
    public int Version { get; set; }

    public string? CurrentVideoPath { get; set; }

    public DateTimeOffset SavedAtUtc { get; set; }

    public string? Status { get; set; }

    public List<RecordedEventDto>? Events { get; set; }
}

public sealed class RecordedEventDto
{
    public long OffsetTicks { get; set; }

    public string? Category { get; set; }

    public string? Message { get; set; }

    public string? OverlayLabel { get; set; }

    public string? ActionKind { get; set; }

    public string? ActionText { get; set; }

    public List<string>? KeyCodes { get; set; }

    public List<string>? ModifierKeyCodes { get; set; }

    public int WheelRotation { get; set; }

    public bool IsHorizontalWheel { get; set; }

    public bool IsQuarantined { get; set; }

    public string? QuarantineReason { get; set; }

    public long Sequence { get; set; }

    public double? ScreenX { get; set; }

    public double? ScreenY { get; set; }

    public int CaptureLeft { get; set; }

    public int CaptureTop { get; set; }

    public int CaptureWidth { get; set; }

    public int CaptureHeight { get; set; }
}

public sealed class LastProjectStateDto
{
    public int Version { get; set; }

    public string? ProjectPath { get; set; }
}
