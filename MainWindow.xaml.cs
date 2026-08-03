using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenRecorderLib;
using SharpHook;
using SharpHook.Data;
using Ellipse = System.Windows.Shapes.Ellipse;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Series4.Desktop;

public partial class MainWindow : Window
{
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const int WmHotkey = 0x0312;
    private const int EmergencyHotkeyId = 0x5344;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkF12 = 0x7B;
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventWheel = 0x0800;
    private const uint MouseEventHorizontalWheel = 0x01000;
    private const uint GetAncestorRoot = 2;
    private static readonly TimeSpan EventOverlayDuration = TimeSpan.FromSeconds(1.6);
    private static readonly SolidColorBrush MouseEventBrush = new(
        Color.FromRgb(18, 183, 106)
    );
    private static readonly SolidColorBrush KeyboardEventBrush = new(
        Color.FromRgb(247, 144, 9)
    );
    private static readonly SolidColorBrush ManualEventBrush = new(
        Color.FromRgb(105, 56, 239)
    );
    private static readonly SolidColorBrush OutsideEventBrush = new(
        Color.FromRgb(240, 68, 56)
    );
    private static readonly SolidColorBrush ReviewWarningBrush = new(
        Color.FromRgb(181, 71, 8)
    );
    private readonly Stopwatch recordingClock = new();
    private readonly DispatcherTimer countdownTimer;
    private readonly DispatcherTimer macroCountdownTimer;
    private readonly DispatcherTimer recordingTimer;
    private readonly DispatcherTimer videoTimer;
    private readonly DispatcherTimer projectSaveTimer;
    private readonly HashSet<KeyCode> pressedKeys = [];
    private readonly Dictionary<MouseButton, PendingMousePress> pendingMousePresses = [];
    private readonly object pendingMouseGate = new();
    private readonly EventSimulator eventSimulator = new();
    private readonly SemaphoreSlim projectSaveGate = new(1, 1);

    private SimpleGlobalHook? globalHook;
    private Task? globalHookRunTask;
    private TaskCompletionSource<bool>? globalHookReadySource;
    private EventHandler<HookEventArgs>? globalHookEnabledHandler;
    private CancellationTokenSource? globalHookStopRequest;
    private OrderedAsyncDrainQueue<HookEventSnapshot>? hookEventQueue;
    private Recorder? recorder;
    private CancellationTokenSource? macroCancellation;
    private Task? macroExecutionTask;
    private CancellationTokenSource? recordingPreparationCancellation;
    private HwndSource? windowSource;
    private string? currentVideoPath;
    private string? pendingVideoPath;
    private string? recordingFailureMessage;
    private string? macroRunPreparationWarning;
    private bool isRecording;
    private bool isPreparingRecording;
    private bool isStartingEventCapture;
    private bool eventCaptureStarted;
    private bool recordingSessionCommitted;
    private bool isFinalizing;
    private bool isPlaying;
    private bool isSeekingVideo;
    private bool isCountingDown;
    private bool isMacroCountingDown;
    private bool isMacroRunning;
    private bool emergencyHotkeyRegistered;
    private bool isStartupLoading = true;
    private bool isVideoReady;
    private bool closeRequested;
    private bool closeAfterSave;
    private bool isCompletingCloseRequest;
    private IntPtr macroTargetWindow;
    private DateTimeOffset countdownEndsAt;
    private DateTimeOffset macroCountdownEndsAt;
    private int captureLeft;
    private int captureTop;
    private int captureWidth = 1;
    private int captureHeight = 1;
    private long nextEventSequence;
    private long projectRevision;
    private long lastCommittedProjectRevision = -1;
    private long recordingSessionId;
    private string? currentProjectPath;
    private MacroProjectStatus currentProjectStatus = MacroProjectStatus.Completed;

    public ObservableCollection<RecordedEvent> RecordedEvents { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        RefreshCaptureBounds();

        countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        countdownTimer.Tick += CountdownTimer_Tick;

        macroCountdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        macroCountdownTimer.Tick += MacroCountdownTimer_Tick;

        recordingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        recordingTimer.Tick += RecordingTimer_Tick;

        videoTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        videoTimer.Tick += VideoTimer_Tick;

        projectSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450),
        };
        projectSaveTimer.Tick += ProjectSaveTimer_Tick;

        RecordedVideo.MediaOpened += RecordedVideo_MediaOpened;
        RecordedVideo.MediaFailed += RecordedVideo_MediaFailed;
        RecordedVideo.MediaEnded += RecordedVideo_MediaEnded;
        RecordedVideo.SizeChanged += (_, _) => RenderEventOverlay(RecordedVideo.Position);
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        isStartupLoading = false;
        StartRecordingButton.IsEnabled = true;
        StartDelayComboBox.IsEnabled = true;
        RecordingStatusText.Text =
            "기록 → 업무 시연 → 종료 → 실행. 네 단계면 됩니다.";
        UpdateEventLogUi();
    }

    private void ScheduleProjectSave()
    {
        if (string.IsNullOrWhiteSpace(currentVideoPath))
        {
            return;
        }

        projectRevision++;
        projectSaveTimer.Stop();
        projectSaveTimer.Start();
    }

    private async void ProjectSaveTimer_Tick(object? sender, EventArgs e)
    {
        projectSaveTimer.Stop();
        await SaveCurrentProjectAsync();
    }

    private async Task<bool> SaveCurrentProjectAsync(
        bool propagateFailure = false
    )
    {
        await projectSaveGate.WaitAsync();
        try
        {
            if (string.IsNullOrWhiteSpace(currentVideoPath))
            {
                return true;
            }

            var videoPath = currentVideoPath;
            var projectPath = currentProjectPath;
            var statusSnapshot = currentProjectStatus;
            var eventsSnapshot = RecordedEvents.ToArray();
            var snapshotRevision = projectRevision;
            if (snapshotRevision <= lastCommittedProjectRevision)
            {
                return true;
            }

            string savedProjectPath;
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                savedProjectPath = await MacroProjectStore.SaveAsync(
                    videoPath,
                    eventsSnapshot,
                    statusSnapshot
                );
            }
            else
            {
                await MacroProjectStore.SaveToPathAsync(
                    projectPath,
                    videoPath,
                    eventsSnapshot,
                    statusSnapshot
                );
                savedProjectPath = projectPath;
            }

            if (
                string.Equals(
                    currentVideoPath,
                    videoPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                lastCommittedProjectRevision = snapshotRevision;
                currentProjectPath = savedProjectPath;
            }
            return true;
        }
        catch (Exception exception)
        {
            RecordingStatusText.Text =
                $"매크로 자동 저장에 실패했습니다: {exception.Message}";
            if (propagateFailure)
            {
                throw;
            }
            return false;
        }
        finally
        {
            projectSaveGate.Release();
        }
    }

    private async void StartRecordingButton_Click(object sender, RoutedEventArgs e)
    {
        if (isCountingDown)
        {
            CancelRecordingCountdown();
            return;
        }

        if (
            isRecording
            || isPreparingRecording
            || isStartupLoading
            || isFinalizing
            || isMacroCountingDown
            || isMacroRunning
        )
        {
            return;
        }

        var delaySeconds = GetSelectedStartDelaySeconds();
        if (delaySeconds <= 0)
        {
            await StartRecordingNowAsync();
            return;
        }

        isCountingDown = true;
        countdownEndsAt = DateTimeOffset.Now.AddSeconds(delaySeconds);
        countdownTimer.Start();
        SetCountdownUi(delaySeconds);
    }

    private async Task StartRecordingNowAsync()
    {
        countdownTimer.Stop();
        isCountingDown = false;
        if (
            isPreparingRecording
            || isRecording
            || isFinalizing
            || isStartupLoading
        )
        {
            return;
        }

        isPreparingRecording = true;
        eventCaptureStarted = false;
        recordingSessionCommitted = false;
        recordingFailureMessage = null;
        isRecording = false;
        isFinalizing = false;
        recordingSessionId++;
        SetPreparingRecordingUi();
        RecordingStatusText.Text = "기존 매크로를 안전하게 저장하고 있습니다…";

        try
        {
            projectSaveTimer.Stop();
            await SaveCurrentProjectAsync();
            RecordedVideo.Pause();
            isPlaying = false;
            PlayPauseButton.Content = "▶ 재생";
            pressedKeys.Clear();
            RefreshCaptureBounds();

            pendingVideoPath = CreateVideoPath();
            OutputPathText.Text = $"다음 저장 위치: {pendingVideoPath}";

            recorder = Recorder.CreateRecorder(CreateRecorderOptions());
            recorder.OnRecordingComplete += Recorder_OnRecordingComplete;
            recorder.OnRecordingFailed += Recorder_OnRecordingFailed;
            recorder.OnStatusChanged += Recorder_OnStatusChanged;

            RecordingStatusText.Text = "녹화 엔진을 준비하고 있습니다…";
            recorder.Record(pendingVideoPath);
            recordingPreparationCancellation?.Dispose();
            recordingPreparationCancellation = new CancellationTokenSource();
            _ = WatchRecordingPreparationAsync(
                recordingSessionId,
                recordingPreparationCancellation.Token
            );
        }
        catch (Exception exception)
        {
            StopGlobalHook();
            CompleteHookEventQueue();
            DisposeRecorder();
            isRecording = false;
            isPreparingRecording = false;
            eventCaptureStarted = false;
            isFinalizing = false;
            pendingVideoPath = null;
            recordingClock.Stop();
            recordingTimer.Stop();
            SetRecordingUi(false);
            if (!string.IsNullOrWhiteSpace(currentVideoPath))
            {
                OutputPathText.Text = $"저장 위치: {currentVideoPath}";
            }
            RecordingStatusText.Text =
                $"기존 로그를 보존했습니다. 녹화를 시작하지 못했습니다: {exception.Message}";
        }
    }

    private async Task WatchRecordingPreparationAsync(
        long sessionId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            await Dispatcher.InvokeAsync(() =>
            {
                if (
                    sessionId == recordingSessionId
                    && isPreparingRecording
                    && !isFinalizing
                )
                {
                    StopRecordingAfterFailure(
                        "녹화 엔진이 15초 안에 시작되지 않았습니다."
                    );
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Recording started or the preparation was cancelled.
        }
    }

    private int GetSelectedStartDelaySeconds()
    {
        if (
            StartDelayComboBox.SelectedItem is ComboBoxItem selectedItem
            && int.TryParse(selectedItem.Tag?.ToString(), out var delaySeconds)
        )
        {
            return delaySeconds;
        }

        return 3;
    }

    private async void CountdownTimer_Tick(object? sender, EventArgs e)
    {
        var remaining = countdownEndsAt - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            countdownTimer.Stop();
            await StartRecordingNowAsync();
            return;
        }

        RecordingTimerText.Text = $"{remaining.TotalSeconds:0.0}초 후";
        RecordingStatusText.Text =
            $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))}초 후 녹화를 시작합니다. 작업 화면을 준비하세요.";
    }

    private void SetCountdownUi(int delaySeconds)
    {
        StartDelayComboBox.IsEnabled = false;
        StartRecordingButton.IsEnabled = true;
        StartRecordingButton.Content = "× 시작 취소";
        StartRecordingButton.Background = new SolidColorBrush(
            Color.FromRgb(217, 45, 32)
        );
        StartRecordingButton.BorderBrush = StartRecordingButton.Background;
        StopRecordingButton.IsEnabled = false;
        RecordingDot.Fill = new SolidColorBrush(Color.FromRgb(247, 144, 9));
        RecordingTimerText.Text = $"{delaySeconds:0.0}초 후";
        RecordingStatusText.Text =
            $"{delaySeconds}초 후 녹화를 시작합니다. 작업 화면을 준비하세요.";
        UpdateEventLogUi();
    }

    private void SetPreparingRecordingUi()
    {
        StartDelayComboBox.IsEnabled = false;
        StartRecordingButton.IsEnabled = false;
        StartRecordingButton.Content = "● 녹화 준비 중";
        StopRecordingButton.IsEnabled = true;
        EventLogList.IsEnabled = false;
        ManualEditorPanel.IsEnabled = false;
        ClearEventsButton.IsEnabled = false;
        RecordingDot.Fill = new SolidColorBrush(Color.FromRgb(247, 144, 9));
        RecordingTimerText.Text = "준비 중";
        RecordingStatusText.Text =
            "녹화 엔진을 준비하고 있습니다. 녹화 시작 신호 후 이벤트 기록이 시작됩니다.";
        UpdateEventLogUi();
    }

    private void BeginEventCapture()
    {
        _ = BeginEventCaptureAsync();
    }

    private async Task BeginEventCaptureAsync()
    {
        if (
            eventCaptureStarted
            || isStartingEventCapture
            || !isPreparingRecording
            || isFinalizing
            || recorder is null
        )
        {
            return;
        }

        isStartingEventCapture = true;
        var sessionId = recordingSessionId;
        var preparationToken =
            recordingPreparationCancellation?.Token ?? CancellationToken.None;
        try
        {
            if (string.IsNullOrWhiteSpace(pendingVideoPath))
            {
                throw new InvalidOperationException(
                    "녹화 파일 경로가 준비되지 않았습니다."
                );
            }

            RecordedVideo.Stop();
            isPlaying = false;
            PlayPauseButton.Content = "▶ 재생";
            VideoPlaceholder.Visibility = Visibility.Visible;
            isVideoReady = false;
            RecordedEvents.Clear();
            nextEventSequence = 0;
            currentVideoPath = pendingVideoPath;
            currentProjectPath = null;
            currentProjectStatus = MacroProjectStatus.InProgress;
            pendingVideoPath = null;
            OutputPathText.Text = $"녹화 중 저장 위치: {currentVideoPath}";
            projectRevision++;
            EventOverlayCanvas.Children.Clear();
            recordingSessionCommitted = true;
            UpdateEventLogUi();

            hookEventQueue = new OrderedAsyncDrainQueue<HookEventSnapshot>(
                DispatchHookEventAsync
            );
            var readySource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            globalHookReadySource = readySource;

            var hook = new SimpleGlobalHook(runAsyncOnBackgroundThread: true);
            var stopRequest = new CancellationTokenSource();
            globalHookStopRequest = stopRequest;
            EventHandler<HookEventArgs> hookEnabledHandler = (_, _) =>
            {
                readySource.TrySetResult(true);
                if (!stopRequest.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    hook.Stop();
                }
                catch
                {
                    // StopGlobalHookAsync retries Stop and reports any failure.
                }
            };
            globalHookEnabledHandler = hookEnabledHandler;
            hook.HookEnabled += hookEnabledHandler;
            hook.MousePressed += GlobalHook_MousePressed;
            hook.MouseReleased += GlobalHook_MouseReleased;
            hook.MouseDragged += GlobalHook_MouseDragged;
            hook.MouseWheel += GlobalHook_MouseWheel;
            hook.KeyPressed += GlobalHook_KeyPressed;
            hook.KeyReleased += GlobalHook_KeyReleased;
            globalHook = hook;
            var hookRunTask = hook.RunAsync();
            globalHookRunTask = hookRunTask;

            await HookStartupReadiness.WaitAsync(
                readySource.Task,
                hookRunTask,
                TimeSpan.FromSeconds(5),
                preparationToken
            );
            preparationToken.ThrowIfCancellationRequested();
            if (
                isFinalizing
                || sessionId != recordingSessionId
                || !ReferenceEquals(globalHook, hook)
            )
            {
                return;
            }

            recordingClock.Restart();
            isPreparingRecording = false;
            isRecording = true;
            eventCaptureStarted = true;
            SetRecordingUi(true);
            recordingTimer.Start();
            AddEvent(
                "시스템",
                "화면 녹화와 이벤트 기록을 시작했습니다.",
                TimeSpan.Zero
            );
            recordingPreparationCancellation?.Cancel();
            _ = MonitorGlobalHookAsync(hook, hookRunTask, sessionId);
        }
        catch (OperationCanceledException) when (
            isFinalizing || sessionId != recordingSessionId
        )
        {
            // StopRecordingAsync owns hook shutdown and queue draining.
        }
        catch (Exception exception)
        {
            StopRecordingAfterFailure(
                $"입력 이벤트 감지를 시작하지 못했습니다: {exception.Message}"
            );
        }
        finally
        {
            isStartingEventCapture = false;
        }
    }

    private async Task MonitorGlobalHookAsync(
        SimpleGlobalHook hook,
        Task runTask,
        long sessionId
    )
    {
        Exception? failure = null;
        try
        {
            await runTask;
        }
        catch (Exception exception)
        {
            failure = exception.GetBaseException();
        }

        await Dispatcher.InvokeAsync(() =>
        {
            if (
                sessionId != recordingSessionId
                || !ReferenceEquals(globalHook, hook)
                || isFinalizing
                || !isRecording
            )
            {
                return;
            }

            StopRecordingAfterFailure(
                failure is null
                    ? "입력 이벤트 감지가 예기치 않게 종료되었습니다."
                    : $"입력 이벤트 감지가 중단되었습니다: {failure.Message}"
            );
        });
    }

    private void CancelRecordingCountdown()
    {
        countdownTimer.Stop();
        isCountingDown = false;
        SetRecordingUi(false);
        RecordingStatusText.Text = "녹화 시작이 취소되었습니다.";
    }

    private void StopRecordingButton_Click(object sender, RoutedEventArgs e)
    {
        StopRecording();
    }

    private void StopRecording()
    {
        _ = StopRecordingAsync();
    }

    private async Task StopRecordingAsync()
    {
        if ((!isRecording && !isPreparingRecording) || isFinalizing)
        {
            return;
        }

        isFinalizing = true;
        recordingPreparationCancellation?.Cancel();

        StartRecordingButton.IsEnabled = false;
        StopRecordingButton.IsEnabled = false;
        RecordingStatusText.Text = "마지막 입력을 반영하고 영상을 마무리하고 있습니다…";
        RecordingTimerText.Text = "저장 중";

        var shutdownErrors = new List<Exception>();
        try
        {
            await StopGlobalHookAsync();
        }
        catch (Exception exception)
        {
            shutdownErrors.Add(exception);
        }

        try
        {
            await CompleteAndDrainHookEventQueueAsync();
        }
        catch (Exception exception)
        {
            shutdownErrors.Add(exception);
        }

        if (shutdownErrors.Count > 0)
        {
            recordingFailureMessage =
                "마지막 입력 감지를 정리하지 못했습니다: "
                + string.Join(
                    " · ",
                    shutdownErrors.Select(error => error.GetBaseException().Message)
                );
        }

        isRecording = false;
        isPreparingRecording = false;
        eventCaptureStarted = false;
        recordingClock.Stop();
        recordingTimer.Stop();

        try
        {
            recorder?.Stop();
        }
        catch (Exception exception)
        {
            CompleteWithFailure($"녹화를 종료하지 못했습니다: {exception.Message}");
            _ = FinishCloseRequestAsync();
        }
    }

    private static RecorderOptions CreateRecorderOptions()
    {
        return new RecorderOptions
        {
            SourceOptions = new SourceOptions
            {
                RecordingSources =
                [
                    new DisplayRecordingSource(DisplayRecordingSource.MainMonitor),
                ],
            },
            OutputOptions = new OutputOptions
            {
                RecorderMode = RecorderMode.Video,
            },
            AudioOptions = new AudioOptions
            {
                IsAudioEnabled = false,
            },
            VideoEncoderOptions = new VideoEncoderOptions
            {
                Bitrate = 6_000_000,
                Framerate = 30,
                IsFixedFramerate = true,
                Encoder = new H264VideoEncoder
                {
                    BitrateMode = H264BitrateControlMode.CBR,
                    EncoderProfile = H264Profile.Main,
                },
                IsHardwareEncodingEnabled = true,
                IsThrottlingDisabled = false,
                IsLowLatencyEnabled = false,
                IsMp4FastStartEnabled = true,
            },
            MouseOptions = new MouseOptions
            {
                IsMousePointerEnabled = true,
                IsMouseClicksDetected = false,
            },
        };
    }

    private static string CreateVideoPath()
    {
        var outputFolder = MacroProjectStore.GetDatedProjectDirectory(
            DateTimeOffset.Now
        );
        Directory.CreateDirectory(outputFolder);
        while (true)
        {
            var candidate = Path.Combine(
                outputFolder,
                $"업무시연_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.mp4"
            );
            if (
                !File.Exists(candidate)
                && !File.Exists(MacroProjectStore.GetSidecarPath(candidate))
            )
            {
                return candidate;
            }
        }
    }

    private void GlobalHook_MousePressed(object? sender, MouseHookEventArgs e)
    {
        if (
            !isRecording
            || !IsPointInsideCapture(e.Data.X, e.Data.Y)
            || IsOwnProcessWindowAt(e.Data.X, e.Data.Y)
        )
        {
            return;
        }

        var actionKind = e.Data.Button switch
        {
            MouseButton.Button1 => MacroActionKind.MouseLeftClick,
            MouseButton.Button2 => MacroActionKind.MouseRightClick,
            MouseButton.Button3 => MacroActionKind.MouseMiddleClick,
            _ => MacroActionKind.None,
        };
        if (actionKind == MacroActionKind.None)
        {
            return;
        }

        lock (pendingMouseGate)
        {
            pendingMousePresses[e.Data.Button] = new PendingMousePress(
                recordingSessionId,
                recordingClock.Elapsed,
                e.Data.Button,
                actionKind,
                e.Data.X,
                e.Data.Y,
                GetPressedModifierCodes()
            );
        }
    }

    private void GlobalHook_MouseDragged(object? sender, MouseHookEventArgs e)
    {
        if (!isRecording)
        {
            return;
        }

        lock (pendingMouseGate)
        {
            if (pendingMousePresses.TryGetValue(e.Data.Button, out var pending))
            {
                pending.MarkDragged(
                    e.Data.X,
                    e.Data.Y,
                    recordingClock.Elapsed
                );
                return;
            }

            foreach (var activePress in pendingMousePresses.Values)
            {
                activePress.MarkDragged(
                    e.Data.X,
                    e.Data.Y,
                    recordingClock.Elapsed
                );
            }
        }
    }

    private void GlobalHook_MouseReleased(object? sender, MouseHookEventArgs e)
    {
        PendingMousePress? pending;
        lock (pendingMouseGate)
        {
            if (!pendingMousePresses.Remove(e.Data.Button, out pending))
            {
                return;
            }
        }

        if (
            !isRecording
            || pending.SessionId != recordingSessionId
        )
        {
            return;
        }

        var button = pending.Button switch
        {
            MouseButton.Button1 => "왼쪽",
            MouseButton.Button2 => "오른쪽",
            MouseButton.Button3 => "가운데",
            _ => pending.Button.ToString(),
        };
        var releaseOffset = recordingClock.Elapsed;
        var distance = Math.Sqrt(
            Math.Pow(e.Data.X - pending.StartX, 2)
            + Math.Pow(e.Data.Y - pending.StartY, 2)
        );
        var isDrag = pending.WasDragged || distance >= 4;
        pending.Complete(e.Data.X, e.Data.Y, releaseOffset, isDrag);
        var mousePath = isDrag ? pending.GetPath() : [];
        var dragDuration = releaseOffset >= pending.Offset
            ? releaseOffset - pending.Offset
            : TimeSpan.Zero;
        AddEventFromHook(
            "마우스",
            isDrag
                ? $"{button} 드래그 감지 · ({pending.StartX}, {pending.StartY}) → ({e.Data.X}, {e.Data.Y})"
                : $"{button} 클릭 · 화면 좌표 ({pending.StartX}, {pending.StartY})",
            pending.StartX,
            pending.StartY,
            isDrag ? $"{button} 드래그" : $"{button} 클릭",
            isDrag ? MacroActionKind.MouseDrag : pending.ActionKind,
            modifierKeyCodes: pending.ModifierKeyCodes,
            explicitOffset: pending.Offset,
            endScreenX: isDrag ? e.Data.X : null,
            endScreenY: isDrag ? e.Data.Y : null,
            dragButton: isDrag ? pending.Button : null,
            dragDuration: isDrag ? dragDuration : null,
            mousePath: mousePath
        );
    }

    private void GlobalHook_MouseWheel(object? sender, MouseWheelHookEventArgs e)
    {
        if (
            !isRecording
            || !IsPointInsideCapture(e.Data.X, e.Data.Y)
            || IsOwnProcessWindowAt(e.Data.X, e.Data.Y)
        )
        {
            return;
        }

        var isHorizontal =
            e.Data.Direction == MouseWheelScrollDirection.Horizontal;
        var wheelDelta = e.Data.Delta == 0 ? 120 : e.Data.Delta;
        var rawRotation = checked((int)e.Data.Rotation * wheelDelta);
        var normalizedRotation = isHorizontal ? -rawRotation : rawRotation;
        var direction = isHorizontal
            ? rawRotation > 0
                ? "왼쪽으로"
                : "오른쪽으로"
            : rawRotation > 0
                ? "위로"
                : "아래로";
        AddEventFromHook(
            "마우스",
            $"휠 {direction} · 화면 좌표 ({e.Data.X}, {e.Data.Y})",
            e.Data.X,
            e.Data.Y,
            $"휠 {direction}",
            MacroActionKind.MouseWheel,
            wheelRotation: normalizedRotation,
            isHorizontalWheel: isHorizontal,
            modifierKeyCodes: GetPressedModifierCodes()
        );
    }

    private void GlobalHook_KeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (!isRecording)
        {
            return;
        }

        lock (pressedKeys)
        {
            pressedKeys.Add(e.Data.KeyCode);
            if (IsEmergencyStopPressed())
            {
                Dispatcher.BeginInvoke(HandleEmergencyStop);
                return;
            }
        }

        if (
            IsModifier(e.Data.KeyCode)
            || !IsForegroundInputInsideCapture()
        )
        {
            return;
        }

        var keyChord = FormatKeyChord(e.Data.KeyCode);
        var keyCodes = BuildKeyChordCodes(e.Data.KeyCode);
        var isNavigationChord =
            e.Data.KeyCode == KeyCode.VcTab
            && keyCodes.Any(
                key =>
                    key
                        is KeyCode.VcLeftAlt
                            or KeyCode.VcRightAlt
                            or KeyCode.VcLeftMeta
                            or KeyCode.VcRightMeta
            );
        AddEventFromHook(
            "키보드",
            $"키 입력 · {keyChord}",
            overlayLabel: $"키 입력 · {keyChord}",
            actionKind: MacroActionKind.KeyStroke,
            actionText: keyChord,
            keyCodes: keyCodes,
            reviewWarningText: isNavigationChord
                ? "창 전환 키가 포함되어 있습니다. 이후 입력 대상 창을 확인하세요."
                : null
        );
    }

    private void GlobalHook_KeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        lock (pressedKeys)
        {
            pressedKeys.Remove(e.Data.KeyCode);
        }
    }

    private bool IsEmergencyStopPressed()
    {
        return IsEmergencyStopKeyChord(pressedKeys);
    }

    private static bool IsEmergencyStopKeyChord(
        IEnumerable<KeyCode> keyCodes
    )
    {
        var keys = keyCodes as IReadOnlyCollection<KeyCode>
            ?? keyCodes.ToArray();
        return keys.Contains(KeyCode.VcF12)
            && (
                keys.Contains(KeyCode.VcLeftControl)
                || keys.Contains(KeyCode.VcRightControl)
            )
            && (
                keys.Contains(KeyCode.VcLeftShift)
                || keys.Contains(KeyCode.VcRightShift)
            );
    }

    private static bool IsModifier(KeyCode keyCode)
    {
        return keyCode is KeyCode.VcLeftControl
            or KeyCode.VcRightControl
            or KeyCode.VcLeftShift
            or KeyCode.VcRightShift
            or KeyCode.VcLeftAlt
            or KeyCode.VcRightAlt
            or KeyCode.VcLeftMeta
            or KeyCode.VcRightMeta;
    }

    private string FormatKeyChord(KeyCode keyCode)
    {
        var parts = new List<string>();
        lock (pressedKeys)
        {
            if (
                pressedKeys.Contains(KeyCode.VcLeftControl)
                || pressedKeys.Contains(KeyCode.VcRightControl)
            )
            {
                parts.Add("Ctrl");
            }
            if (
                pressedKeys.Contains(KeyCode.VcLeftShift)
                || pressedKeys.Contains(KeyCode.VcRightShift)
            )
            {
                parts.Add("Shift");
            }
            if (
                pressedKeys.Contains(KeyCode.VcLeftAlt)
                || pressedKeys.Contains(KeyCode.VcRightAlt)
            )
            {
                parts.Add("Alt");
            }
            if (
                pressedKeys.Contains(KeyCode.VcLeftMeta)
                || pressedKeys.Contains(KeyCode.VcRightMeta)
            )
            {
                parts.Add("Win");
            }
        }

        parts.Add(keyCode.ToString().Replace("Vc", string.Empty));
        return string.Join(" + ", parts);
    }

    private KeyCode[] BuildKeyChordCodes(KeyCode keyCode)
    {
        var keyCodes = new List<KeyCode>();
        lock (pressedKeys)
        {
            AddPressedModifier(keyCodes, KeyCode.VcLeftControl, KeyCode.VcRightControl);
            AddPressedModifier(keyCodes, KeyCode.VcLeftShift, KeyCode.VcRightShift);
            AddPressedModifier(keyCodes, KeyCode.VcLeftAlt, KeyCode.VcRightAlt);
            AddPressedModifier(keyCodes, KeyCode.VcLeftMeta, KeyCode.VcRightMeta);
        }

        keyCodes.Add(keyCode);
        return [.. keyCodes];
    }

    private KeyCode[] GetPressedModifierCodes()
    {
        var keyCodes = new List<KeyCode>();
        lock (pressedKeys)
        {
            AddPressedModifier(keyCodes, KeyCode.VcLeftControl, KeyCode.VcRightControl);
            AddPressedModifier(keyCodes, KeyCode.VcLeftShift, KeyCode.VcRightShift);
            AddPressedModifier(keyCodes, KeyCode.VcLeftAlt, KeyCode.VcRightAlt);
            AddPressedModifier(keyCodes, KeyCode.VcLeftMeta, KeyCode.VcRightMeta);
        }

        return [.. keyCodes];
    }

    private void AddPressedModifier(
        ICollection<KeyCode> keyCodes,
        KeyCode leftModifier,
        KeyCode rightModifier
    )
    {
        if (pressedKeys.Contains(leftModifier))
        {
            keyCodes.Add(leftModifier);
        }
        else if (pressedKeys.Contains(rightModifier))
        {
            keyCodes.Add(rightModifier);
        }
    }

    private void AddEventFromHook(
        string category,
        string message,
        double? screenX = null,
        double? screenY = null,
        string? overlayLabel = null,
        MacroActionKind actionKind = MacroActionKind.None,
        string? actionText = null,
        KeyCode[]? keyCodes = null,
        int wheelRotation = 0,
        bool isHorizontalWheel = false,
        KeyCode[]? modifierKeyCodes = null,
        TimeSpan? explicitOffset = null,
        bool isQuarantined = false,
        string? quarantineReason = null,
        string? reviewWarningText = null,
        double? endScreenX = null,
        double? endScreenY = null,
        MouseButton? dragButton = null,
        TimeSpan? dragDuration = null,
        MousePathPoint[]? mousePath = null
    )
    {
        var queue = hookEventQueue;
        if (queue is null)
        {
            return;
        }

        _ = queue.TryEnqueue(
            new HookEventSnapshot(
                recordingSessionId,
                explicitOffset ?? recordingClock.Elapsed,
                category,
                message,
                screenX,
                screenY,
                overlayLabel,
                actionKind,
                actionText,
                keyCodes?.ToArray() ?? [],
                wheelRotation,
                isHorizontalWheel,
                modifierKeyCodes?.ToArray() ?? [],
                isQuarantined,
                quarantineReason,
                reviewWarningText,
                captureLeft,
                captureTop,
                captureWidth,
                captureHeight,
                endScreenX,
                endScreenY,
                dragButton,
                dragDuration,
                mousePath?.ToArray() ?? []
            )
        );
    }

    private ValueTask DispatchHookEventAsync(HookEventSnapshot snapshot)
    {
        var operation = Dispatcher.InvokeAsync(
            () => CommitHookEvent(snapshot),
            DispatcherPriority.Normal
        );
        return new ValueTask(operation.Task);
    }

    private void CommitHookEvent(HookEventSnapshot snapshot)
    {
        if (
            snapshot.SessionId != recordingSessionId
            || !eventCaptureStarted
            || !isRecording
        )
        {
            return;
        }

        AddEvent(
            snapshot.Category,
            snapshot.Message,
            snapshot.Offset,
            snapshot.ScreenX,
            snapshot.ScreenY,
            snapshot.OverlayLabel,
            snapshot.ActionKind,
            snapshot.ActionText,
            snapshot.KeyCodes,
            snapshot.WheelRotation,
            snapshot.IsHorizontalWheel,
            snapshot.ModifierKeyCodes,
            snapshot.IsQuarantined,
            snapshot.QuarantineReason,
            snapshot.ReviewWarningText,
            snapshot.CaptureLeft,
            snapshot.CaptureTop,
            snapshot.CaptureWidth,
            snapshot.CaptureHeight,
            snapshot.EndScreenX,
            snapshot.EndScreenY,
            snapshot.DragButton,
            snapshot.DragDuration,
            snapshot.MousePath
        );
    }

    private void AddEvent(
        string category,
        string message,
        TimeSpan? offset = null,
        double? screenX = null,
        double? screenY = null,
        string? overlayLabel = null,
        MacroActionKind actionKind = MacroActionKind.None,
        string? actionText = null,
        KeyCode[]? keyCodes = null,
        int wheelRotation = 0,
        bool isHorizontalWheel = false,
        KeyCode[]? modifierKeyCodes = null,
        bool isQuarantined = false,
        string? quarantineReason = null,
        string? reviewWarningText = null,
        int? eventCaptureLeft = null,
        int? eventCaptureTop = null,
        int? eventCaptureWidth = null,
        int? eventCaptureHeight = null,
        double? endScreenX = null,
        double? endScreenY = null,
        MouseButton? dragButton = null,
        TimeSpan? dragDuration = null,
        MousePathPoint[]? mousePath = null
    )
    {
        var recordedEvent = new RecordedEvent
        {
            Offset = offset ?? recordingClock.Elapsed,
            Category = category,
            Message = message,
            OverlayLabel = overlayLabel,
            ActionKind = actionKind,
            ActionText = actionText,
            KeyCodes = keyCodes?.ToArray() ?? [],
            WheelRotation = wheelRotation,
            IsHorizontalWheel = isHorizontalWheel,
            ModifierKeyCodes = modifierKeyCodes?.ToArray() ?? [],
            IsQuarantined = isQuarantined,
            QuarantineReason = quarantineReason,
            ReviewWarningText = reviewWarningText,
            Sequence = ++nextEventSequence,
            ScreenX = screenX,
            ScreenY = screenY,
            EndScreenX = endScreenX,
            EndScreenY = endScreenY,
            DragButton = dragButton,
            DragDuration = dragDuration,
            MousePath = mousePath?.ToArray() ?? [],
            CaptureLeft = eventCaptureLeft ?? captureLeft,
            CaptureTop = eventCaptureTop ?? captureTop,
            CaptureWidth = eventCaptureWidth ?? captureWidth,
            CaptureHeight = eventCaptureHeight ?? captureHeight,
        };

        ApplyEventPolicy(recordedEvent);
        InsertEventChronologically(recordedEvent);
        UpdateEventLogUi();
        EventLogList.ScrollIntoView(recordedEvent);
        ScheduleProjectSave();
    }

    private void InsertEventChronologically(RecordedEvent recordedEvent)
    {
        if (!isRecording)
        {
            ClearExecutionResults();
        }
        if (
            RecordedEvents.Count == 0
            || RecordedEvents[^1].Offset < recordedEvent.Offset
            || (
                RecordedEvents[^1].Offset == recordedEvent.Offset
                && RecordedEvents[^1].Sequence <= recordedEvent.Sequence
            )
        )
        {
            RecordedEvents.Add(recordedEvent);
            return;
        }

        var insertionIndex = 0;
        while (
            insertionIndex < RecordedEvents.Count
            && (
                RecordedEvents[insertionIndex].Offset < recordedEvent.Offset
                || (
                    RecordedEvents[insertionIndex].Offset == recordedEvent.Offset
                    && RecordedEvents[insertionIndex].Sequence
                        <= recordedEvent.Sequence
                )
            )
        )
        {
            insertionIndex++;
        }

        RecordedEvents.Insert(insertionIndex, recordedEvent);
    }

    private void DeleteEventButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RecordedEvent recordedEvent })
        {
            DeleteEvent(recordedEvent);
        }

        e.Handled = true;
    }

    private void MoveEventUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RecordedEvent recordedEvent })
        {
            MoveEvent(recordedEvent, -1);
        }

        e.Handled = true;
    }

    private void MoveEventDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RecordedEvent recordedEvent })
        {
            MoveEvent(recordedEvent, 1);
        }

        e.Handled = true;
    }

    private void MoveEvent(RecordedEvent recordedEvent, int direction)
    {
        var currentIndex = RecordedEvents.IndexOf(recordedEvent);
        var targetIndex = currentIndex + direction;
        if (
            currentIndex < 0
            || targetIndex < 0
            || targetIndex >= RecordedEvents.Count
        )
        {
            return;
        }

        ClearExecutionResults();
        var adjacentEvent = RecordedEvents[targetIndex];
        if (recordedEvent.Offset == adjacentEvent.Offset)
        {
            (recordedEvent.Sequence, adjacentEvent.Sequence) = (
                adjacentEvent.Sequence,
                recordedEvent.Sequence
            );
        }
        else
        {
            (recordedEvent.Offset, adjacentEvent.Offset) = (
                adjacentEvent.Offset,
                recordedEvent.Offset
            );
        }

        NormalizeEventOrder();
        EventLogList.SelectedItem = recordedEvent;
        EventLogList.ScrollIntoView(recordedEvent);
        RenderEventOverlay(RecordedVideo.Position);
        ScheduleProjectSave();
    }

    private void NormalizeEventOrder()
    {
        var ordered = RecordedEvents
            .OrderBy(item => item.Offset)
            .ThenBy(item => item.Sequence)
            .ToList();
        for (var targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
        {
            var currentIndex = RecordedEvents.IndexOf(ordered[targetIndex]);
            if (currentIndex != targetIndex)
            {
                RecordedEvents.Move(currentIndex, targetIndex);
            }
        }
    }

    private void EventLogList_KeyDown(object sender, KeyEventArgs e)
    {
        if (
            e.Key == Key.Delete
            && EventLogList.SelectedItem is RecordedEvent recordedEvent
        )
        {
            DeleteEvent(recordedEvent);
            e.Handled = true;
        }
    }

    private void ClearEventsButton_Click(object sender, RoutedEventArgs e)
    {
        EventLogList.SelectedItem = null;
        RecordedEvents.Clear();
        UpdateEventLogUi();
        RenderEventOverlay(RecordedVideo.Position);
        ScheduleProjectSave();
    }

    private void DeleteEvent(RecordedEvent recordedEvent)
    {
        if (ReferenceEquals(EventLogList.SelectedItem, recordedEvent))
        {
            EventLogList.SelectedItem = null;
        }

        if (!RecordedEvents.Remove(recordedEvent))
        {
            return;
        }

        ClearExecutionResults();
        UpdateEventLogUi();
        RenderEventOverlay(RecordedVideo.Position);
        ScheduleProjectSave();
    }

    private void UpdateEventLogUi()
    {
        EventCountText.Text = RecordedEvents.Count.ToString();
        var blockedCount = RecordedEvents.Count(
            recordedEvent => recordedEvent.IsQuarantined
        );
        BlockedCountText.Text = $"실패 {blockedCount}";
        BlockedCountBadge.Visibility = blockedCount > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        RunMacroButton.Content = "▶ 실행";
        var editingLocked =
            isStartupLoading
            || closeRequested
            || isRecording
            || isPreparingRecording
            || isFinalizing
            || isCountingDown
            || isMacroCountingDown
            || isMacroRunning;
        EventLogList.IsEnabled = !editingLocked;
        EmptyEventHint.Visibility = RecordedEvents.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ManualEditorPanel.IsEnabled = !editingLocked;
        ClearEventsButton.IsEnabled =
            RecordedEvents.Count > 0 && !editingLocked;
        RunMacroButton.IsEnabled =
            RecordedEvents.Any(recordedEvent => recordedEvent.IsExecutable)
            && !isRecording
            && !isPreparingRecording
            && !isStartupLoading
            && !isFinalizing
            && !isCountingDown
            && !isMacroCountingDown
            && !isMacroRunning;
    }

    private async void RunMacroButton_Click(object sender, RoutedEventArgs e)
    {
        if (
            isRecording
            || isFinalizing
            || isCountingDown
            || isMacroCountingDown
            || isMacroRunning
        )
        {
            return;
        }

        if (!RecordedEvents.Any(recordedEvent => recordedEvent.IsExecutable))
        {
            RecordingStatusText.Text = "실행 가능한 키보드 또는 마우스 항목이 없습니다.";
            return;
        }

        RefreshCaptureBounds();
        ApplyEventPolicies();
        UpdateEventLogUi();
        macroRunPreparationWarning = string.IsNullOrWhiteSpace(currentVideoPath)
            ? "연결된 영상이 없어 현재 이벤트는 저장되지 않습니다. 현재 메모리 내용으로 실행합니다."
            : null;
        try
        {
            projectSaveTimer.Stop();
            if (!string.IsNullOrWhiteSpace(currentVideoPath))
            {
                await SaveCurrentProjectAsync(propagateFailure: true);
            }
        }
        catch
        {
            macroRunPreparationWarning =
                "최신 편집 내용을 저장하지 못했습니다. 현재 메모리의 이벤트로 실행합니다.";
        }

        if (isPlaying)
        {
            RecordedVideo.Pause();
            isPlaying = false;
            PlayPauseButton.Content = "▶ 재생";
        }

        var requestedDelaySeconds = GetSelectedStartDelaySeconds();
        var requiresInputTarget = RecordedEvents.Any(
            item =>
                item.IsExecutable
                && item.ActionKind
                    is MacroActionKind.TextEntry
                        or MacroActionKind.KeyStroke
        );
        var delaySeconds =
            requestedDelaySeconds <= 0 && requiresInputTarget
                ? 3
                : requestedDelaySeconds;
        macroTargetWindow = IntPtr.Zero;
        if (delaySeconds <= 0)
        {
            StartMacroExecution();
            return;
        }

        isMacroCountingDown = true;
        macroCountdownEndsAt = DateTimeOffset.Now.AddSeconds(delaySeconds);
        macroCountdownTimer.Start();
        SetMacroCountdownUi(delaySeconds);
        if (requestedDelaySeconds <= 0 && requiresInputTarget)
        {
            RecordingStatusText.Text +=
                " 키보드 입력 대상을 선택할 시간을 확보하기 위해 최소 3초를 적용했습니다.";
        }
    }

    private void StopMacroButton_Click(object sender, RoutedEventArgs e)
    {
        if (isMacroCountingDown)
        {
            CancelMacroCountdown();
            return;
        }

        if (!isMacroRunning)
        {
            return;
        }

        RecordingStatusText.Text = "매크로 실행을 중지하고 있습니다…";
        macroCancellation?.Cancel();
    }

    private void MacroCountdownTimer_Tick(object? sender, EventArgs e)
    {
        RememberMacroTargetWindow();
        var remaining = macroCountdownEndsAt - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            macroCountdownTimer.Stop();
            isMacroCountingDown = false;
            StartMacroExecution();
            return;
        }

        RecordingTimerText.Text = $"{remaining.TotalSeconds:0.0}초 후";
        RecordingStatusText.Text =
            $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))}초 후 실행합니다. 지금 대상 입력창이나 업무 화면을 선택하세요.";
    }

    private void SetMacroCountdownUi(int delaySeconds)
    {
        StartDelayComboBox.IsEnabled = false;
        StartRecordingButton.IsEnabled = false;
        StopRecordingButton.IsEnabled = false;
        RunMacroButton.IsEnabled = false;
        StopMacroButton.IsEnabled = true;
        EventLogList.IsEnabled = false;
        ManualEditorPanel.IsEnabled = false;
        ClearEventsButton.IsEnabled = false;
        PlayPauseButton.IsEnabled = false;
        VideoPositionSlider.IsEnabled = false;
        PlaybackSpeedComboBox.IsEnabled = false;
        RecordingDot.Fill = new SolidColorBrush(Color.FromRgb(247, 144, 9));
        RecordingTimerText.Text = $"{delaySeconds:0.0}초 후";
        var executableCount = RecordedEvents.Count(
            recordedEvent => recordedEvent.IsExecutable
        );
        RecordingStatusText.Text =
            $"{executableCount}개 이벤트를 {delaySeconds}초 후 실행합니다. 지금 대상 입력창이나 업무 화면을 선택하세요.";
    }

    private void CancelMacroCountdown()
    {
        macroCountdownTimer.Stop();
        isMacroCountingDown = false;
        RestoreUiAfterMacro("매크로 실행 시작이 취소되었습니다.", "대기 중");
    }

    private void StartMacroExecution()
    {
        RefreshCaptureBounds();
        ApplyEventPolicies();

        var executableEvents = RecordedEvents
            .Where(recordedEvent => recordedEvent.IsExecutable)
            .OrderBy(recordedEvent => recordedEvent.Offset)
            .ThenBy(recordedEvent => recordedEvent.Sequence)
            .ToList();
        if (executableEvents.Count == 0)
        {
            isMacroCountingDown = false;
            RestoreUiAfterMacro("실행 가능한 항목이 없습니다.", "대기 중");
            return;
        }

        foreach (var recordedEvent in RecordedEvents)
        {
            recordedEvent.LastExecutionResult = recordedEvent.IsQuarantined
                ? $"건너뜀 · {recordedEvent.QuarantineReason ?? "기술적으로 실행할 수 없는 이벤트입니다."}"
                : null;
            recordedEvent.LastExecutionFailed = recordedEvent.IsQuarantined;
        }

        macroCountdownTimer.Stop();
        isMacroCountingDown = false;
        isMacroRunning = true;
        macroCancellation?.Dispose();
        macroCancellation = new CancellationTokenSource();
        SetMacroRunningUi(executableEvents.Count);
        macroExecutionTask = ExecuteMacroAsync(executableEvents, macroCancellation);
    }

    private void SetMacroRunningUi(int eventCount)
    {
        StartDelayComboBox.IsEnabled = false;
        StartRecordingButton.IsEnabled = false;
        StopRecordingButton.IsEnabled = false;
        RunMacroButton.IsEnabled = false;
        StopMacroButton.IsEnabled = true;
        EventLogList.IsEnabled = false;
        ManualEditorPanel.IsEnabled = false;
        ClearEventsButton.IsEnabled = false;
        PlayPauseButton.IsEnabled = false;
        VideoPositionSlider.IsEnabled = false;
        PlaybackSpeedComboBox.IsEnabled = false;
        RecordingDot.Fill = new SolidColorBrush(Color.FromRgb(18, 183, 106));
        RecordingTimerText.Text = $"0/{eventCount}";
        const string runSummary = "매크로 실행 중입니다.";
        RecordingStatusText.Text = string.IsNullOrWhiteSpace(
            macroRunPreparationWarning
        )
            ? $"{runSummary} 중지하려면 Ctrl + Shift + F12를 누르세요."
            : $"{macroRunPreparationWarning} {runSummary} Ctrl + Shift + F12로 중지할 수 있습니다.";
    }

    private async Task ExecuteMacroAsync(
        IReadOnlyList<RecordedEvent> executableEvents,
        CancellationTokenSource cancellationSource
    )
    {
        var clock = Stopwatch.StartNew();
        var baseOffset = executableEvents[0].Offset;
        var successCount = 0;
        var failedCount = 0;
        try
        {
            for (var index = 0; index < executableEvents.Count; index++)
            {
                var recordedEvent = executableEvents[index];
                await WaitUntilMacroOffsetAsync(
                    recordedEvent.Offset - baseOffset,
                    clock,
                    cancellationSource.Token
                );
                cancellationSource.Token.ThrowIfCancellationRequested();

                RecordingTimerText.Text = $"{index + 1}/{executableEvents.Count}";
                RecordingStatusText.Text =
                    $"매크로 실행 중 · {index + 1}/{executableEvents.Count} · {recordedEvent.Message}";
                try
                {
                    await Task.Run(
                        () => ExecuteMacroEvent(
                            recordedEvent,
                            cancellationSource.Token
                        ),
                        cancellationSource.Token
                    );
                    successCount++;
                    recordedEvent.LastExecutionFailed = false;
                    recordedEvent.LastExecutionResult = "실행 완료";
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (MacroInputCleanupException exception)
                {
                    failedCount++;
                    recordedEvent.LastExecutionFailed = true;
                    recordedEvent.LastExecutionResult =
                        $"입력 상태 정리 실패 · {exception.Message}";
                    throw;
                }
                catch (Exception exception)
                {
                    failedCount++;
                    recordedEvent.LastExecutionFailed = true;
                    recordedEvent.LastExecutionResult =
                        $"건너뜀 · {exception.Message}";
                }
            }

            RestoreUiAfterMacro(
                $"실행을 마쳤습니다. 성공 {successCount} · 실패 {failedCount}.",
                "실행 완료"
            );
        }
        catch (OperationCanceledException)
        {
            RestoreUiAfterMacro("매크로 실행을 중지했습니다.", "중지됨");
        }
        catch (MacroInputCleanupException exception)
        {
            RestoreUiAfterMacro(
                $"입력 상태를 정리하지 못해 실행을 중단했습니다: {exception.Message}",
                "실행 오류"
            );
        }
        catch (Exception exception)
        {
            RestoreUiAfterMacro(
                $"매크로 실행에 실패했습니다: {exception.Message}",
                "실행 오류"
            );
        }
        finally
        {
            if (ReferenceEquals(macroCancellation, cancellationSource))
            {
                macroCancellation.Dispose();
                macroCancellation = null;
            }
        }
    }

    private async Task WaitUntilMacroOffsetAsync(
        TimeSpan targetOffset,
        Stopwatch clock,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = targetOffset - clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            var delay = remaining > TimeSpan.FromMilliseconds(200)
                ? TimeSpan.FromMilliseconds(200)
                : remaining;
            await Task.Delay(delay, cancellationToken);
        }
    }

    private void ExecuteMacroEvent(
        RecordedEvent recordedEvent,
        CancellationToken cancellationToken
    )
    {
        ValidateMacroEventTarget(recordedEvent);
        switch (recordedEvent.ActionKind)
        {
            case MacroActionKind.TextEntry:
                if (recordedEvent.ActionText is not { Length: > 0 } text)
                {
                    throw new InvalidOperationException("입력할 텍스트가 비어 있습니다.");
                }
                ExecuteUnicodeTextWithCleanup(text, cancellationToken);
                break;
            case MacroActionKind.KeyStroke:
                if (recordedEvent.KeyCodes.Length == 0)
                {
                    throw new InvalidOperationException("실행할 키 정보가 비어 있습니다.");
                }
                ExecuteKeyStrokeWithCleanup(
                    recordedEvent.KeyCodes,
                    cancellationToken
                );
                break;
            case MacroActionKind.MouseLeftClick:
                ExecuteMouseClick(recordedEvent, MouseButton.Button1);
                break;
            case MacroActionKind.MouseRightClick:
                ExecuteMouseClick(recordedEvent, MouseButton.Button2);
                break;
            case MacroActionKind.MouseMiddleClick:
                ExecuteMouseClick(recordedEvent, MouseButton.Button3);
                break;
            case MacroActionKind.MouseDrag:
                ExecuteMouseDrag(recordedEvent, cancellationToken);
                break;
            case MacroActionKind.MouseWheel:
                ExecuteMouseWheel(recordedEvent);
                break;
        }
    }

    private void ValidateMacroEventTarget(RecordedEvent recordedEvent)
    {
        if (
            recordedEvent.ActionKind
                is MacroActionKind.TextEntry
                    or MacroActionKind.KeyStroke
        )
        {
            if (!TryValidateForegroundInput(out var targetError))
            {
                throw new InvalidOperationException(targetError);
            }
            return;
        }

        if (
            recordedEvent.ActionKind
                is not (
                    MacroActionKind.MouseLeftClick
                    or MacroActionKind.MouseRightClick
                    or MacroActionKind.MouseMiddleClick
                    or MacroActionKind.MouseDrag
                    or MacroActionKind.MouseWheel
                )
        )
        {
            return;
        }

        int x;
        int y;
        if (!TryGetRecordedCursorPosition(recordedEvent, out x, out y))
        {
            if (!GetCursorPosition(out x, out y))
            {
                throw new InvalidOperationException(
                    "현재 마우스 위치를 확인하지 못했습니다."
                );
            }
        }

        if (IsOwnProcessWindowAt(x, y))
        {
            throw new InvalidOperationException(
                "매크로 앱 자체를 가리키는 이벤트라 이 이벤트만 건너뜁니다."
            );
        }
    }

    private void ExecuteMouseClick(
        RecordedEvent recordedEvent,
        MouseButton mouseButton
    )
    {
        ExecuteWithRecordedModifiers(recordedEvent, () =>
        {
            if (
                !TryGetRecordedCursorPosition(recordedEvent, out var x, out var y)
                && !GetCursorPosition(out x, out y)
            )
            {
                throw new InvalidOperationException(
                    "실행할 마우스 위치를 확인하지 못했습니다."
                );
            }
            MoveCursorTo(x, y);

            var (downFlag, upFlag) = mouseButton switch
            {
                MouseButton.Button1 => (MouseEventLeftDown, MouseEventLeftUp),
                MouseButton.Button2 => (MouseEventRightDown, MouseEventRightUp),
                MouseButton.Button3 => (MouseEventMiddleDown, MouseEventMiddleUp),
                _ => throw new InvalidOperationException(
                    $"지원하지 않는 마우스 버튼입니다: {mouseButton}"
                ),
            };
            Exception? clickError = null;
            var downSent = false;
            var releaseFailed = false;
            try
            {
                SendNativeMouseInputs(CreateNativeMouseInput(downFlag));
                downSent = true;
                Thread.Sleep(50);
            }
            catch (Exception exception)
            {
                clickError = exception;
            }
            finally
            {
                if (downSent)
                {
                    try
                    {
                        SendNativeMouseInputs(CreateNativeMouseInput(upFlag));
                    }
                    catch (Exception releaseException)
                    {
                        releaseFailed = true;
                        clickError = clickError is null
                            ? releaseException
                            : new AggregateException(
                                "마우스 클릭과 버튼 해제에 모두 실패했습니다.",
                                clickError,
                                releaseException
                            );
                    }
                }
            }

            if (clickError is not null)
            {
                throw releaseFailed
                    ? new MacroInputCleanupException(
                        "마우스 버튼을 해제하지 못했습니다.",
                        clickError
                    )
                    : clickError;
            }
        });
    }

    private void ExecuteMouseWheel(RecordedEvent recordedEvent)
    {
        ExecuteWithRecordedModifiers(recordedEvent, () =>
        {
            if (
                !TryGetRecordedCursorPosition(recordedEvent, out var x, out var y)
                && !GetCursorPosition(out x, out y)
            )
            {
                throw new InvalidOperationException(
                    "실행할 마우스 위치를 확인하지 못했습니다."
                );
            }
            MoveCursorTo(x, y);

            var rotation = recordedEvent.WheelRotation == 0
                ? 120
                : recordedEvent.WheelRotation;
            SendNativeMouseInputs(
                CreateNativeMouseInput(
                    recordedEvent.IsHorizontalWheel
                        ? MouseEventHorizontalWheel
                        : MouseEventWheel,
                    rotation
                )
            );
        });
    }

    private void ExecuteMouseDrag(
        RecordedEvent recordedEvent,
        CancellationToken cancellationToken
    )
    {
        if (recordedEvent.DragButton is not { } button)
        {
            throw new InvalidOperationException(
                "드래그 버튼 정보가 없습니다."
            );
        }
        if (recordedEvent.MousePath.Length < 2)
        {
            throw new InvalidOperationException(
                "드래그 경로가 없습니다."
            );
        }

        ExecuteWithRecordedModifiers(recordedEvent, () =>
        {
            try
            {
                MouseDragReplayEngine.Execute(
                    recordedEvent.MousePath,
                    button,
                    MoveCursorTo,
                    pressedButton =>
                    {
                        var (downFlag, _) = GetMouseButtonFlags(pressedButton);
                        SendNativeMouseInputs(CreateNativeMouseInput(downFlag));
                    },
                    releasedButton =>
                    {
                        var (_, upFlag) = GetMouseButtonFlags(releasedButton);
                        SendNativeMouseInputs(CreateNativeMouseInput(upFlag));
                    },
                    cancellationToken
                );
            }
            catch (MouseButtonReleaseException exception)
            {
                throw new MacroInputCleanupException(
                    "드래그 후 마우스 버튼을 해제하지 못했습니다.",
                    exception
                );
            }
        });
    }

    private static (uint Down, uint Up) GetMouseButtonFlags(
        MouseButton button
    )
    {
        return button switch
        {
            MouseButton.Button1 => (MouseEventLeftDown, MouseEventLeftUp),
            MouseButton.Button2 => (MouseEventRightDown, MouseEventRightUp),
            MouseButton.Button3 => (MouseEventMiddleDown, MouseEventMiddleUp),
            _ => throw new InvalidOperationException(
                $"지원하지 않는 마우스 버튼입니다: {button}"
            ),
        };
    }

    private void ExecuteWithRecordedModifiers(
        RecordedEvent recordedEvent,
        Action pointerAction
    )
    {
        var pressedModifiers = new List<KeyCode>();
        Exception? actionError = null;
        var releaseErrors = new List<Exception>();
        try
        {
            foreach (
                var modifier in recordedEvent.ModifierKeyCodes
                    .Where(IsModifier)
                    .Distinct()
            )
            {
                EnsureSimulationSucceeded(
                    eventSimulator.SimulateKeyPress(modifier),
                    $"{modifier} 누르기"
                );
                pressedModifiers.Add(modifier);
            }

            pointerAction();
        }
        catch (Exception exception)
        {
            actionError = exception;
        }
        finally
        {
            for (var index = pressedModifiers.Count - 1; index >= 0; index--)
            {
                try
                {
                    EnsureSimulationSucceeded(
                        eventSimulator.SimulateKeyRelease(pressedModifiers[index]),
                        $"{pressedModifiers[index]} 떼기"
                    );
                }
                catch (Exception exception)
                {
                    releaseErrors.Add(exception);
                }
            }
        }

        if (releaseErrors.Count > 0)
        {
            var errors = new List<Exception>();
            if (actionError is not null)
            {
                errors.Add(actionError);
            }
            errors.AddRange(releaseErrors);
            throw new MacroInputCleanupException(
                "마우스 동작 후 modifier 입력 상태를 완전히 정리하지 못했습니다.",
                errors.Count == 1
                    ? errors[0]
                    : new AggregateException(errors)
            );
        }

        if (actionError is not null)
        {
            throw actionError;
        }
    }

    private void ExecuteKeyStrokeWithCleanup(
        IReadOnlyList<KeyCode> keyCodes,
        CancellationToken cancellationToken
    )
    {
        var attemptedKeys = new List<KeyCode>();
        Exception? pressError = null;
        var releaseErrors = new List<Exception>();
        try
        {
            foreach (var keyCode in keyCodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attemptedKeys.Add(keyCode);
                EnsureSimulationSucceeded(
                    eventSimulator.SimulateKeyPress(keyCode),
                    $"{keyCode} 누르기"
                );
            }
        }
        catch (Exception exception)
        {
            pressError = exception;
        }
        finally
        {
            for (var index = attemptedKeys.Count - 1; index >= 0; index--)
            {
                try
                {
                    EnsureSimulationSucceeded(
                        eventSimulator.SimulateKeyRelease(attemptedKeys[index]),
                        $"{attemptedKeys[index]} 떼기"
                    );
                }
                catch (Exception exception)
                {
                    releaseErrors.Add(exception);
                }
            }
        }

        if (releaseErrors.Count > 0)
        {
            var errors = new List<Exception>();
            if (pressError is not null)
            {
                errors.Add(pressError);
            }
            errors.AddRange(releaseErrors);
            throw new MacroInputCleanupException(
                "키 입력 상태를 완전히 정리하지 못했습니다.",
                errors.Count == 1
                    ? errors[0]
                    : new AggregateException(errors)
            );
        }

        if (pressError is not null)
        {
            throw pressError;
        }
    }

    private static void ExecuteUnicodeTextWithCleanup(
        string text,
        CancellationToken cancellationToken
    )
    {
        foreach (var codeUnit in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Exception? pressError = null;
            Exception? releaseError = null;
            try
            {
                SendNativeKeyboardInput(
                    CreateUnicodeKeyboardInput(codeUnit, keyUp: false),
                    $"문자 U+{(int)codeUnit:X4} 누르기"
                );
            }
            catch (Exception exception)
            {
                pressError = exception;
            }
            finally
            {
                try
                {
                    SendNativeKeyboardInput(
                        CreateUnicodeKeyboardInput(codeUnit, keyUp: true),
                        $"문자 U+{(int)codeUnit:X4} 떼기"
                    );
                }
                catch (Exception exception)
                {
                    releaseError = exception;
                }
            }

            if (releaseError is not null)
            {
                throw new MacroInputCleanupException(
                    "텍스트 키 입력 상태를 완전히 정리하지 못했습니다.",
                    pressError is null
                        ? releaseError
                        : new AggregateException(pressError, releaseError)
                );
            }

            if (pressError is not null)
            {
                throw pressError;
            }
        }
    }

    private static bool TryGetRecordedCursorPosition(
        RecordedEvent recordedEvent,
        out int x,
        out int y
    )
    {
        if (
            recordedEvent.ScreenX is not double screenX
            || recordedEvent.ScreenY is not double screenY
            || !double.IsFinite(screenX)
            || !double.IsFinite(screenY)
        )
        {
            x = 0;
            y = 0;
            return false;
        }

        x = (int)Math.Round(Math.Clamp(screenX, int.MinValue, (double)int.MaxValue));
        y = (int)Math.Round(Math.Clamp(screenY, int.MinValue, (double)int.MaxValue));
        return true;
    }

    private void MoveCursorTo(int x, int y)
    {
        if (!SetCursorPos(x, y))
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                $"마우스 포인터를 ({x}, {y})로 이동하지 못했습니다."
            );
        }

        if (!GetCursorPosition(out var actualX, out var actualY))
        {
            throw new InvalidOperationException(
                "이동 후 실제 마우스 위치를 확인하지 못했습니다."
            );
        }
        if (actualX != x || actualY != y)
        {
            throw new InvalidOperationException(
                $"요청한 좌표 ({x}, {y}) 대신 ({actualX}, {actualY})로 이동되어 이 이벤트만 건너뜁니다."
            );
        }
        if (IsOwnProcessWindowAt(actualX, actualY))
        {
            throw new InvalidOperationException(
                "매크로 앱 자체를 가리키는 이벤트라 이 이벤트만 건너뜁니다."
            );
        }
    }

    private static NativeInput CreateNativeMouseInput(
        uint flags,
        int mouseData = 0
    )
    {
        return new NativeInput
        {
            Type = InputMouse,
            Data = new NativeInputUnion
            {
                Mouse = new NativeMouseInput
                {
                    MouseData = unchecked((uint)mouseData),
                    Flags = flags,
                },
            },
        };
    }

    private static NativeInput CreateUnicodeKeyboardInput(char codeUnit, bool keyUp)
    {
        return new NativeInput
        {
            Type = InputKeyboard,
            Data = new NativeInputUnion
            {
                Keyboard = new NativeKeyboardInput
                {
                    ScanCode = codeUnit,
                    Flags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0),
                },
            },
        };
    }

    private static void SendNativeKeyboardInput(NativeInput input, string operation)
    {
        var inputs = new[] { input };
        var sent = SendInput(1, inputs, Marshal.SizeOf<NativeInput>());
        if (sent != 1)
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                $"{operation} 입력을 전송하지 못했습니다."
            );
        }
    }

    private static void SendNativeMouseInputs(params NativeInput[] inputs)
    {
        var sent = SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeInput>()
        );
        if (sent != inputs.Length)
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                $"마우스 입력 {inputs.Length}개 중 {sent}개만 전송했습니다."
            );
        }
    }

    private static void EnsureSimulationSucceeded(
        UioHookResult result,
        string operation
    )
    {
        if (result != UioHookResult.Success)
        {
            throw new InvalidOperationException($"{operation} 실패 ({result})");
        }
    }

    private void RestoreUiAfterMacro(string statusMessage, string timerText)
    {
        isMacroCountingDown = false;
        isMacroRunning = false;
        macroTargetWindow = IntPtr.Zero;
        macroRunPreparationWarning = null;
        EventLogList.IsEnabled = true;
        ManualEditorPanel.IsEnabled = true;
        StopMacroButton.IsEnabled = false;
        PlayPauseButton.IsEnabled = RecordedVideo.Source is not null;
        VideoPositionSlider.IsEnabled = RecordedVideo.Source is not null;
        PlaybackSpeedComboBox.IsEnabled = RecordedVideo.Source is not null;
        SetRecordingUi(false);
        RecordingStatusText.Text = statusMessage;
        RecordingTimerText.Text = timerText;
        UpdateEventLogUi();
    }

    private void AddManualEventButton_Click(object sender, RoutedEventArgs e)
    {
        if (
            !TryReadManualEditor(
                out var activity,
                out var offset,
                out var actionTag,
                out var keyCodes
            )
        )
        {
            return;
        }

        var recordedEvent = new RecordedEvent
        {
            Offset = offset,
            Category = "수동",
            Message = activity,
            OverlayLabel = activity,
            CaptureLeft = captureLeft,
            CaptureTop = captureTop,
            CaptureWidth = captureWidth,
            CaptureHeight = captureHeight,
            Sequence = ++nextEventSequence,
        };
        ConfigureManualAction(recordedEvent, actionTag, activity, keyCodes);
        ApplyEventPolicy(recordedEvent);
        InsertEventChronologically(recordedEvent);
        UpdateEventLogUi();
        EventLogList.SelectedItem = recordedEvent;
        EventLogList.ScrollIntoView(recordedEvent);
        SetManualEditorMessage(
            $"실행 항목을 {offset.TotalSeconds:0.000}초에 추가했습니다.",
            isSuccess: true
        );
        RenderEventOverlay(RecordedVideo.Position);
        ScheduleProjectSave();
    }

    private void UpdateSelectedEventButton_Click(object sender, RoutedEventArgs e)
    {
        if (EventLogList.SelectedItem is not RecordedEvent recordedEvent)
        {
            SetManualEditorMessage("수정할 로그를 먼저 선택하세요.");
            return;
        }

        if (
            !TryReadManualEditor(
                out var activity,
                out var offset,
                out var actionTag,
                out var keyCodes
            )
        )
        {
            return;
        }

        var previousActionTag = GetManualActionTag(recordedEvent);
        var previousActivity = GetManualEditorActivity(recordedEvent);
        var actionKindChanged =
            !string.Equals(
                previousActionTag,
                actionTag,
                StringComparison.Ordinal
            );
        var activityChanged =
            !string.Equals(
                previousActivity,
                activity,
                StringComparison.Ordinal
            );
        var actionPayloadChanged = actionKindChanged || activityChanged;
        var repairsPolicyBlockedPointer =
            IsPointerActionTag(actionTag)
            && recordedEvent.IsQuarantined
            && string.Equals(
                recordedEvent.QuarantineReason,
                MacroEventPolicy.GetTechnicalBlockReason(recordedEvent),
                StringComparison.Ordinal
            );
        ClearExecutionResults();
        recordedEvent.Offset = offset;
        if (actionPayloadChanged || repairsPolicyBlockedPointer)
        {
            if (
                recordedEvent.ActionKind == MacroActionKind.MouseDrag
                && actionTag == "MouseDrag"
            )
            {
                if (!string.IsNullOrWhiteSpace(activity))
                {
                    recordedEvent.Message = activity;
                    recordedEvent.OverlayLabel = activity;
                }
            }
            else if (
                IsPointerActionTag(actionTag)
                && recordedEvent.ActionKind
                    is MacroActionKind.MouseLeftClick
                        or MacroActionKind.MouseRightClick
                        or MacroActionKind.MouseMiddleClick
                        or MacroActionKind.MouseWheel
            )
            {
                if (actionKindChanged || repairsPolicyBlockedPointer)
                {
                    recordedEvent.IsQuarantined = false;
                    recordedEvent.QuarantineReason = null;
                    ApplyPointerActionKind(recordedEvent, actionTag);
                }
                UpdatePointerPresentation(recordedEvent, activity);
            }
            else
            {
                recordedEvent.IsQuarantined = false;
                recordedEvent.QuarantineReason = null;
                ConfigureManualAction(recordedEvent, actionTag, activity, keyCodes);
            }
        }

        ApplyEventPolicy(recordedEvent);
        NormalizeEventOrder();
        ManualEditorModeText.Text = $"선택: {recordedEvent.TimeText}";
        SetManualEditorMessage(
            $"선택한 로그를 {offset.TotalSeconds:0.000}초로 수정했습니다.",
            isSuccess: true
        );

        if (RecordedVideo.Source is not null)
        {
            RecordedVideo.Position = offset;
            UpdateVideoTimeText();
        }
        RenderEventOverlay(offset);
        EventLogList.ScrollIntoView(recordedEvent);
        UpdateEventLogUi();
        ScheduleProjectSave();
    }

    private void UseCurrentVideoTimeButton_Click(object sender, RoutedEventArgs e)
    {
        var currentTime = isRecording
            ? recordingClock.Elapsed
            : RecordedVideo.Source is not null
                ? RecordedVideo.Position
                : TimeSpan.Zero;
        ManualSecondsTextBox.Text = currentTime.TotalSeconds.ToString(
            "0.000",
            CultureInfo.InvariantCulture
        );
        SetManualEditorMessage("현재 시점을 입력했습니다.", isSuccess: true);
    }

    private bool TryReadManualEditor(
        out string activity,
        out TimeSpan offset,
        out string actionTag,
        out KeyCode[] keyCodes
    )
    {
        actionTag = GetSelectedManualActionTag();
        var rawActivity = ManualActivityTextBox.Text;
        activity = actionTag == "TextEntry" ? rawActivity : rawActivity.Trim();
        offset = TimeSpan.Zero;
        keyCodes = [];

        var requiresActivity = actionTag is "None" or "TextEntry" or "KeyStroke";
        var activityMissing = actionTag == "TextEntry"
            ? activity.Length == 0
            : string.IsNullOrWhiteSpace(activity);
        if (requiresActivity && activityMissing)
        {
            SetManualEditorMessage(
                actionTag switch
                {
                    "TextEntry" => "입력할 텍스트를 적으세요.",
                    "KeyStroke" => "키 또는 단축키를 적으세요. 예: Ctrl+S",
                    _ => "활동 내용을 입력하세요.",
                }
            );
            ManualActivityTextBox.Focus();
            return false;
        }

        if (
            actionTag == "KeyStroke"
            && !TryParseKeyChord(activity, out keyCodes, out var keyError)
        )
        {
            SetManualEditorMessage(keyError);
            ManualActivityTextBox.Focus();
            ManualActivityTextBox.SelectAll();
            return false;
        }

        var secondsText = ManualSecondsTextBox.Text.Trim();
        var parsed =
            double.TryParse(
                secondsText,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out var seconds
            )
            || double.TryParse(
                secondsText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out seconds
            );
        if (
            !parsed
            || !double.IsFinite(seconds)
            || seconds < 0
            || seconds > TimeSpan.MaxValue.TotalSeconds
        )
        {
            SetManualEditorMessage("시점은 0 이상의 숫자로 입력하세요.");
            ManualSecondsTextBox.Focus();
            ManualSecondsTextBox.SelectAll();
            return false;
        }

        offset = TimeSpan.FromSeconds(seconds);
        if (
            RecordedVideo.Source is not null
            && RecordedVideo.NaturalDuration.HasTimeSpan
            && offset > RecordedVideo.NaturalDuration.TimeSpan
        )
        {
            SetManualEditorMessage(
                $"영상 종료 후 {offset.TotalSeconds:0.000}초에 실행됩니다."
            );
        }

        return true;
    }

    private void PopulateManualEditor(RecordedEvent recordedEvent)
    {
        var actionTag = GetManualActionTag(recordedEvent);
        SelectManualActionTag(actionTag);
        ManualActivityTextBox.Text = GetManualEditorActivity(recordedEvent);
        ManualSecondsTextBox.Text = recordedEvent.Offset.TotalSeconds.ToString(
            "0.000",
            CultureInfo.InvariantCulture
        );
        ManualEditorModeText.Text = $"선택: {recordedEvent.TimeText}";
        UpdateSelectedEventButton.IsEnabled = true;
        SetManualEditorMessage("내용이나 시간을 바꾼 뒤 ‘선택 수정’을 누르세요.");
    }

    private static string GetManualEditorActivity(RecordedEvent recordedEvent)
    {
        return recordedEvent.ActionKind switch
        {
            MacroActionKind.TextEntry or MacroActionKind.KeyStroke =>
                recordedEvent.ActionText ?? string.Empty,
            MacroActionKind.None => recordedEvent.Message,
            _ => recordedEvent.ActionText ?? string.Empty,
        };
    }

    private void ResetManualEditor()
    {
        ManualActivityTextBox.Clear();
        SelectManualActionTag("None");
        var currentTime = isRecording
            ? recordingClock.Elapsed
            : RecordedVideo.Source is not null
                ? RecordedVideo.Position
                : TimeSpan.Zero;
        ManualSecondsTextBox.Text = currentTime.TotalSeconds.ToString(
            "0.000",
            CultureInfo.InvariantCulture
        );
        ManualEditorModeText.Text = "새 이벤트";
        UpdateSelectedEventButton.IsEnabled = false;
        SetManualEditorMessage("활동과 시점을 입력해 새 로그를 추가할 수 있습니다.");
    }

    private void ManualActionTypeComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e
    )
    {
        if (ManualActivityLabelText is null || ManualValidationText is null)
        {
            return;
        }

        var actionTag = GetSelectedManualActionTag();
        ManualActivityLabelText.Text = actionTag switch
        {
            "TextEntry" => "입력할 텍스트",
            "KeyStroke" => "키 조합 (예: Ctrl+S)",
            "MouseLeftClick" or "MouseRightClick" or "MouseMiddleClick" =>
                "활동 설명 (선택)",
            "MouseWheelUp"
            or "MouseWheelDown"
            or "MouseWheelLeft"
            or "MouseWheelRight" => "활동 설명 (선택)",
            _ => "활동 내용",
        };
        SetManualEditorMessage(
            actionTag switch
            {
                "TextEntry" => "한글을 포함한 텍스트를 현재 입력 위치에 그대로 입력합니다.",
                "KeyStroke" => "Ctrl+S, Alt+F4, Enter처럼 +로 키를 연결하세요.",
                "MouseLeftClick" or "MouseRightClick" or "MouseMiddleClick" =>
                    "실행 시점의 현재 커서 위치에서 클릭합니다.",
                "MouseWheelUp"
                or "MouseWheelDown"
                or "MouseWheelLeft"
                or "MouseWheelRight" =>
                    "실행 시점의 현재 커서 위치에서 한 칸 스크롤합니다.",
                _ => "메모는 영상에 표시되지만 매크로로 실행되지는 않습니다.",
            }
        );
    }

    private string GetSelectedManualActionTag()
    {
        return ManualActionTypeComboBox?.SelectedItem is ComboBoxItem selectedItem
            ? selectedItem.Tag?.ToString() ?? "None"
            : "None";
    }

    private void SelectManualActionTag(string actionTag)
    {
        if (ManualActionTypeComboBox is null)
        {
            return;
        }

        foreach (var item in ManualActionTypeComboBox.Items.OfType<ComboBoxItem>())
        {
            if (
                string.Equals(
                    item.Tag?.ToString(),
                    actionTag,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                ManualActionTypeComboBox.SelectedItem = item;
                return;
            }
        }

        ManualActionTypeComboBox.SelectedIndex = 0;
    }

    private static string GetManualActionTag(RecordedEvent recordedEvent)
    {
        return recordedEvent.ActionKind switch
        {
            MacroActionKind.TextEntry => "TextEntry",
            MacroActionKind.KeyStroke => "KeyStroke",
            MacroActionKind.MouseLeftClick => "MouseLeftClick",
            MacroActionKind.MouseRightClick => "MouseRightClick",
            MacroActionKind.MouseMiddleClick => "MouseMiddleClick",
            MacroActionKind.MouseDrag => "MouseDrag",
            MacroActionKind.MouseWheel
                when recordedEvent.IsHorizontalWheel
                    && recordedEvent.WheelRotation >= 0 =>
                "MouseWheelRight",
            MacroActionKind.MouseWheel when recordedEvent.IsHorizontalWheel =>
                "MouseWheelLeft",
            MacroActionKind.MouseWheel when recordedEvent.WheelRotation >= 0 =>
                "MouseWheelUp",
            MacroActionKind.MouseWheel => "MouseWheelDown",
            _ => "None",
        };
    }

    private void ConfigureManualAction(
        RecordedEvent recordedEvent,
        string actionTag,
        string activity,
        KeyCode[] keyCodes
    )
    {
        recordedEvent.ActionKind = MacroActionKind.None;
        recordedEvent.ActionText = null;
        recordedEvent.KeyCodes = [];
        recordedEvent.ModifierKeyCodes = [];
        recordedEvent.WheelRotation = 0;
        recordedEvent.IsHorizontalWheel = false;
        recordedEvent.ScreenX = null;
        recordedEvent.ScreenY = null;
        recordedEvent.EndScreenX = null;
        recordedEvent.EndScreenY = null;
        recordedEvent.DragButton = null;
        recordedEvent.DragDuration = null;
        recordedEvent.MousePath = [];
        recordedEvent.IsQuarantined = false;
        recordedEvent.QuarantineReason = null;
        recordedEvent.ReviewWarningText = null;

        switch (actionTag)
        {
            case "TextEntry":
                recordedEvent.Category = "키보드";
                recordedEvent.Message = $"텍스트 입력 · {activity}";
                recordedEvent.OverlayLabel = $"텍스트 입력 · {activity}";
                recordedEvent.ActionKind = MacroActionKind.TextEntry;
                recordedEvent.ActionText = activity;
                break;
            case "KeyStroke":
                recordedEvent.Category = "키보드";
                recordedEvent.Message = $"키 입력 · {activity}";
                recordedEvent.OverlayLabel = $"키 입력 · {activity}";
                recordedEvent.ActionKind = MacroActionKind.KeyStroke;
                recordedEvent.ActionText = activity;
                recordedEvent.KeyCodes = keyCodes.ToArray();
                break;
            case "MouseLeftClick":
                ConfigureManualPointerAction(
                    recordedEvent,
                    MacroActionKind.MouseLeftClick,
                    "왼쪽 클릭",
                    activity
                );
                break;
            case "MouseRightClick":
                ConfigureManualPointerAction(
                    recordedEvent,
                    MacroActionKind.MouseRightClick,
                    "오른쪽 클릭",
                    activity
                );
                break;
            case "MouseMiddleClick":
                ConfigureManualPointerAction(
                    recordedEvent,
                    MacroActionKind.MouseMiddleClick,
                    "가운데 클릭",
                    activity
                );
                break;
            case "MouseWheelUp":
                ConfigureManualPointerAction(
                    recordedEvent,
                    MacroActionKind.MouseWheel,
                    "휠 위로",
                    activity,
                    120
                );
                break;
            case "MouseWheelDown":
                ConfigureManualPointerAction(
                    recordedEvent,
                    MacroActionKind.MouseWheel,
                    "휠 아래로",
                    activity,
                    -120
                );
                break;
            case "MouseWheelLeft":
                ConfigureManualPointerAction(
                    recordedEvent,
                    MacroActionKind.MouseWheel,
                    "휠 왼쪽으로",
                    activity,
                    -120,
                    isHorizontalWheel: true
                );
                break;
            case "MouseWheelRight":
                ConfigureManualPointerAction(
                    recordedEvent,
                    MacroActionKind.MouseWheel,
                    "휠 오른쪽으로",
                    activity,
                    120,
                    isHorizontalWheel: true
                );
                break;
            default:
                recordedEvent.Category = "수동";
                recordedEvent.Message = activity;
                recordedEvent.OverlayLabel = activity;
                break;
        }
    }

    private void ConfigureManualPointerAction(
        RecordedEvent recordedEvent,
        MacroActionKind actionKind,
        string actionLabel,
        string activity,
        int wheelRotation = 0,
        bool isHorizontalWheel = false
    )
    {
        var description = string.IsNullOrWhiteSpace(activity)
            ? actionLabel
            : $"{actionLabel} · {activity}";
        recordedEvent.Category = "마우스";
        recordedEvent.Message = $"{description} · 실행 시점 현재 커서 위치";
        recordedEvent.OverlayLabel = description;
        recordedEvent.ActionKind = actionKind;
        recordedEvent.ActionText = activity;
        recordedEvent.WheelRotation = wheelRotation;
        recordedEvent.IsHorizontalWheel = isHorizontalWheel;
    }

    private static void UpdatePointerPresentation(
        RecordedEvent recordedEvent,
        string activity
    )
    {
        var actionLabel = recordedEvent.ActionKind switch
        {
            MacroActionKind.MouseLeftClick => "왼쪽 클릭",
            MacroActionKind.MouseRightClick => "오른쪽 클릭",
            MacroActionKind.MouseMiddleClick => "가운데 클릭",
            MacroActionKind.MouseWheel when recordedEvent.IsHorizontalWheel =>
                recordedEvent.WheelRotation >= 0
                    ? "휠 오른쪽으로"
                    : "휠 왼쪽으로",
            MacroActionKind.MouseWheel =>
                recordedEvent.WheelRotation >= 0
                    ? "휠 위로"
                    : "휠 아래로",
            _ => throw new InvalidOperationException(
                "마우스 이벤트가 아닌 항목의 설명을 수정할 수 없습니다."
            ),
        };
        var description = string.IsNullOrWhiteSpace(activity)
            ? actionLabel
            : $"{actionLabel} · {activity}";
        var location = recordedEvent.ScreenX is double x
            && recordedEvent.ScreenY is double y
            ? $"화면 좌표 ({x:0}, {y:0})"
            : "실행 시점 현재 커서 위치";
        recordedEvent.Category = "마우스";
        recordedEvent.Message = $"{description} · {location}";
        recordedEvent.OverlayLabel = description;
        recordedEvent.ActionText = activity;
    }

    private static bool IsPointerActionTag(string actionTag)
    {
        return actionTag
            is "MouseLeftClick"
                or "MouseRightClick"
                or "MouseMiddleClick"
                or "MouseWheelUp"
                or "MouseWheelDown"
                or "MouseWheelLeft"
                or "MouseWheelRight";
    }

    private static void ApplyPointerActionKind(
        RecordedEvent recordedEvent,
        string actionTag
    )
    {
        recordedEvent.ActionKind = actionTag switch
        {
            "MouseLeftClick" => MacroActionKind.MouseLeftClick,
            "MouseRightClick" => MacroActionKind.MouseRightClick,
            "MouseMiddleClick" => MacroActionKind.MouseMiddleClick,
            "MouseWheelUp"
            or "MouseWheelDown"
            or "MouseWheelLeft"
            or "MouseWheelRight" => MacroActionKind.MouseWheel,
            _ => throw new InvalidOperationException(
                $"지원하지 않는 마우스 동작입니다: {actionTag}"
            ),
        };
        recordedEvent.WheelRotation = actionTag switch
        {
            "MouseWheelDown" or "MouseWheelLeft" => -120,
            "MouseWheelUp" or "MouseWheelRight" => 120,
            _ => 0,
        };
        recordedEvent.IsHorizontalWheel =
            actionTag is "MouseWheelLeft" or "MouseWheelRight";
    }

    private static bool TryParseKeyChord(
        string text,
        out KeyCode[] keyCodes,
        out string error
    )
    {
        var parsedKeys = new List<KeyCode>();
        foreach (
            var token in text.Split(
                '+',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            if (!TryParseKeyToken(token, out var keyCode))
            {
                keyCodes = [];
                error = $"‘{token}’ 키를 인식하지 못했습니다. 예: Ctrl+S, Enter, F5";
                return false;
            }

            if (!parsedKeys.Contains(keyCode))
            {
                parsedKeys.Add(keyCode);
            }
        }

        if (parsedKeys.Count == 0 || parsedKeys.All(IsModifier))
        {
            keyCodes = [];
            error = "마지막에 실행할 일반 키를 하나 포함하세요. 예: Ctrl+S";
            return false;
        }

        if (IsEmergencyStopKeyChord(parsedKeys))
        {
            keyCodes = [];
            error = "Ctrl+Shift+F12는 긴급 중지 전용이라 실행 항목으로 쓸 수 없습니다.";
            return false;
        }

        keyCodes = [.. parsedKeys];
        error = string.Empty;
        return true;
    }

    private static bool TryParseKeyToken(string token, out KeyCode keyCode)
    {
        var normalized = token.Trim().Replace(" ", string.Empty).ToUpperInvariant();
        keyCode = normalized switch
        {
            "CTRL" or "CONTROL" or "컨트롤" => KeyCode.VcLeftControl,
            "SHIFT" or "시프트" => KeyCode.VcLeftShift,
            "ALT" or "알트" => KeyCode.VcLeftAlt,
            "WIN" or "WINDOWS" or "META" or "윈도우" or "윈" =>
                KeyCode.VcLeftMeta,
            "ENTER" or "RETURN" or "엔터" => KeyCode.VcEnter,
            "ESC" or "ESCAPE" or "이스케이프" => KeyCode.VcEscape,
            "SPACE" or "스페이스" => KeyCode.VcSpace,
            "TAB" or "탭" => KeyCode.VcTab,
            "BACKSPACE" or "백스페이스" => KeyCode.VcBackspace,
            "DELETE" or "DEL" or "삭제" => KeyCode.VcDelete,
            "INSERT" or "INS" => KeyCode.VcInsert,
            "HOME" => KeyCode.VcHome,
            "END" => KeyCode.VcEnd,
            "PAGEUP" or "PGUP" => KeyCode.VcPageUp,
            "PAGEDOWN" or "PGDN" => KeyCode.VcPageDown,
            "UP" or "↑" or "위" or "위쪽" => KeyCode.VcUp,
            "DOWN" or "↓" or "아래" or "아래쪽" => KeyCode.VcDown,
            "LEFT" or "←" or "왼쪽" => KeyCode.VcLeft,
            "RIGHT" or "→" or "오른쪽" => KeyCode.VcRight,
            "한영" or "한/영" or "HANGUL" => KeyCode.VcKana,
            "한자" or "HANJA" => KeyCode.VcHanja,
            _ => KeyCode.VcUndefined,
        };
        if (keyCode != KeyCode.VcUndefined)
        {
            return true;
        }

        var enumName = normalized.StartsWith("VC", StringComparison.Ordinal)
            ? normalized
            : $"Vc{normalized}";
        return Enum.TryParse(enumName, ignoreCase: true, out keyCode)
            && keyCode != KeyCode.VcUndefined;
    }

    private void SetManualEditorMessage(string message, bool isSuccess = false)
    {
        ManualValidationText.Text = message;
        ManualValidationText.Foreground = new SolidColorBrush(
            isSuccess
                ? Color.FromRgb(2, 122, 72)
                : Color.FromRgb(102, 112, 133)
        );
    }

    private void Recorder_OnRecordingComplete(
        object? sender,
        RecordingCompleteEventArgs e
    )
    {
        var completedRecorder = sender as Recorder;
        Dispatcher.BeginInvoke(
            new Action(async () =>
            {
                if (
                    completedRecorder is null
                    || !ReferenceEquals(recorder, completedRecorder)
                )
                {
                    return;
                }

                CleanupRecordingCapture();
                isFinalizing = false;
                var committed = recordingSessionCommitted;
                recordingSessionCommitted = false;
                SetRecordingUi(false);
                DisposeRecorder();
                if (committed)
                {
                    currentProjectStatus = recordingFailureMessage is null
                        ? MacroProjectStatus.Completed
                        : MacroProjectStatus.Failed;
                    projectRevision++;
                    if (currentProjectStatus != MacroProjectStatus.Completed)
                    {
                        ApplyEventPolicies();
                    }
                    LoadRecordedVideo(e.FilePath);
                    var saved = await SaveCurrentProjectAsync();
                    RecordingStatusText.Text = saved
                        ? recordingFailureMessage is null
                            ? $"영상과 {RecordedEvents.Count}개 이벤트를 저장했습니다."
                            : $"{recordingFailureMessage} 영상과 현재 이벤트는 저장했습니다."
                        : "영상은 저장했지만 매크로 프로젝트 저장에 실패했습니다.";
                }
                else
                {
                    pendingVideoPath = null;
                    if (!string.IsNullOrWhiteSpace(currentVideoPath))
                    {
                        OutputPathText.Text = $"저장 위치: {currentVideoPath}";
                    }
                    RecordingStatusText.Text =
                        recordingFailureMessage
                        ?? "녹화 준비를 취소했고 기존 로그를 보존했습니다.";
                }

                recordingFailureMessage = null;
                await FinishCloseRequestAsync();
            })
        );
    }

    private void Recorder_OnRecordingFailed(
        object? sender,
        RecordingFailedEventArgs e
    )
    {
        var failedRecorder = sender as Recorder;
        Dispatcher.BeginInvoke(
            new Action(async () =>
            {
                if (
                    failedRecorder is null
                    || !ReferenceEquals(recorder, failedRecorder)
                )
                {
                    return;
                }

                CompleteWithFailure($"영상 녹화에 실패했습니다: {e.Error}");
                if (recordingSessionCommitted)
                {
                    await SaveCurrentProjectAsync();
                }
                recordingSessionCommitted = false;
                await FinishCloseRequestAsync();
            })
        );
    }

    private void Recorder_OnStatusChanged(object? sender, RecordingStatusEventArgs e)
    {
        var statusRecorder = sender as Recorder;
        Dispatcher.BeginInvoke(() =>
        {
            if (
                statusRecorder is null
                || !ReferenceEquals(recorder, statusRecorder)
            )
            {
                return;
            }

            if (e.Status == RecorderStatus.Recording)
            {
                BeginEventCapture();
            }
            else if (e.Status == RecorderStatus.Finishing && isFinalizing)
            {
                RecordingStatusText.Text = "영상을 마무리하고 있습니다…";
            }
            else if (isRecording)
            {
                RecordingStatusText.Text = $"화면과 이벤트 기록 중 · {e.Status}";
            }
        });
    }

    private void StopRecordingAfterFailure(string message)
    {
        recordingFailureMessage = message;
        if (
            recorder is not null
            && (isRecording || isPreparingRecording)
            && !isFinalizing
        )
        {
            StopRecording();
            RecordingStatusText.Text =
                $"{message} 영상을 안전하게 마무리하고 있습니다…";
            return;
        }

        CompleteWithFailure(message);
    }

    private void CompleteWithFailure(string message)
    {
        CleanupRecordingCapture();
        isFinalizing = false;
        pendingVideoPath = null;
        if (recordingSessionCommitted)
        {
            currentProjectStatus = MacroProjectStatus.Failed;
            projectRevision++;
            ApplyEventPolicies();
        }
        SetRecordingUi(false);
        DisposeRecorder();
        RecordingStatusText.Text = recordingSessionCommitted
            ? $"{message} 현재 이벤트는 보존했습니다."
            : $"{message} 기존 로그는 그대로 보존했습니다.";
        isVideoReady = recordingSessionCommitted
            ? false
            : isVideoReady;
    }

    private void CleanupRecordingCapture()
    {
        isRecording = false;
        isPreparingRecording = false;
        isStartingEventCapture = false;
        eventCaptureStarted = false;
        recordingClock.Stop();
        recordingTimer.Stop();
        StopGlobalHook();
        CompleteHookEventQueue();
        lock (pendingMouseGate)
        {
            pendingMousePresses.Clear();
        }
    }

    private void SetRecordingUi(bool recording)
    {
        var macroLocked = isMacroCountingDown || isMacroRunning;
        StartRecordingButton.IsEnabled =
            !recording
            && !isFinalizing
            && !macroLocked
            && !isStartupLoading
            && !closeRequested;
        StartRecordingButton.Content = "● 기록";
        StartRecordingButton.Background = new SolidColorBrush(
            Color.FromRgb(23, 105, 224)
        );
        StartRecordingButton.BorderBrush = StartRecordingButton.Background;
        StartDelayComboBox.IsEnabled =
            !recording
            && !isFinalizing
            && !macroLocked
            && !isStartupLoading
            && !closeRequested;
        StopRecordingButton.IsEnabled = recording;
        RecordingDot.Fill = recording
            ? System.Windows.Media.Brushes.Red
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(152, 162, 179)
            );
        RecordingTimerText.Text = recording ? "00:00.0" : "대기 중";
        RecordingStatusText.Text = recording
            ? "주 모니터 화면과 업무 이벤트를 기록하고 있습니다."
            : "녹화를 시작하면 화면과 업무 이벤트가 함께 기록됩니다.";
        UpdateEventLogUi();
    }

    private void RecordingTimer_Tick(object? sender, EventArgs e)
    {
        var elapsed = recordingClock.Elapsed;
        RecordingTimerText.Text =
            $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds / 100}";
    }

    private void LoadRecordedVideo(string path)
    {
        currentVideoPath = path;
        isVideoReady = false;
        OutputPathText.Text = $"저장 위치: {path}";
        VideoStatusText.Text = Path.GetFileName(path);
        VideoPlaceholder.Visibility = Visibility.Collapsed;
        RecordedVideo.Source = new Uri(path, UriKind.Absolute);
        RecordedVideo.Position = TimeSpan.Zero;
        PlayPauseButton.IsEnabled = false;
        VideoPositionSlider.IsEnabled = false;
        PlaybackSpeedComboBox.IsEnabled = false;
        ApplyPlaybackSpeed();
        RecordedVideo.Play();
        RecordedVideo.Pause();
        videoTimer.Start();
    }

    private void RecordedVideo_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (RecordedVideo.NaturalDuration.HasTimeSpan)
        {
            isVideoReady = true;
            ApplyEventPolicies();
            VideoPositionSlider.Maximum =
                RecordedVideo.NaturalDuration.TimeSpan.TotalSeconds;
            UpdateVideoTimeText();
            RenderEventOverlay(RecordedVideo.Position);
            PlayPauseButton.IsEnabled = true;
            VideoPositionSlider.IsEnabled = true;
            PlaybackSpeedComboBox.IsEnabled = true;
            UpdateEventLogUi();
        }
    }

    private void RecordedVideo_MediaFailed(
        object? sender,
        ExceptionRoutedEventArgs e
    )
    {
        if (
            !string.IsNullOrWhiteSpace(currentVideoPath)
            && !isRecording
            && !isPreparingRecording
            && !isFinalizing
        )
        {
            currentProjectStatus = MacroProjectStatus.Failed;
            projectRevision++;
            ApplyEventPolicies();
            ScheduleProjectSave();
        }
        isVideoReady = false;
        isPlaying = false;
        PlayPauseButton.IsEnabled = false;
        VideoPositionSlider.IsEnabled = false;
        PlaybackSpeedComboBox.IsEnabled = false;
        VideoStatusText.Text = "영상을 열지 못했습니다.";
        RecordingStatusText.Text =
            $"영상을 열지 못했지만 이벤트 로그는 실행할 수 있습니다: {e.ErrorException.Message}";
        UpdateEventLogUi();
    }

    private void RecordedVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        RecordedVideo.Position = TimeSpan.Zero;
        RecordedVideo.Pause();
        isPlaying = false;
        PlayPauseButton.Content = "▶ 재생";
        RenderEventOverlay(RecordedVideo.Position);
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (RecordedVideo.Source is null)
        {
            return;
        }

        if (isPlaying)
        {
            RecordedVideo.Pause();
            PlayPauseButton.Content = "▶ 재생";
        }
        else
        {
            ApplyPlaybackSpeed();
            RecordedVideo.Play();
            PlayPauseButton.Content = "Ⅱ 일시정지";
        }

        isPlaying = !isPlaying;
    }

    private void PlaybackSpeedComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e
    )
    {
        ApplyPlaybackSpeed();
    }

    private void ApplyPlaybackSpeed()
    {
        if (
            PlaybackSpeedComboBox?.SelectedItem is ComboBoxItem selectedItem
            && double.TryParse(
                selectedItem.Tag?.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var speedRatio
            )
            && speedRatio > 0
        )
        {
            RecordedVideo.SpeedRatio = speedRatio;
        }
    }

    private void VideoTimer_Tick(object? sender, EventArgs e)
    {
        if (RecordedVideo.Source is null || isSeekingVideo)
        {
            return;
        }

        isSeekingVideo = true;
        VideoPositionSlider.Value = RecordedVideo.Position.TotalSeconds;
        isSeekingVideo = false;
        UpdateVideoTimeText();
        RenderEventOverlay(RecordedVideo.Position);
    }

    private void VideoPositionSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e
    )
    {
        if (
            isSeekingVideo
            || RecordedVideo.Source is null
            || !VideoPositionSlider.IsMouseCaptureWithin
        )
        {
            return;
        }

        RecordedVideo.Position = TimeSpan.FromSeconds(e.NewValue);
        UpdateVideoTimeText();
        RenderEventOverlay(RecordedVideo.Position);
    }

    private void EventLogList_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e
    )
    {
        if (EventLogList.SelectedItem is not RecordedEvent recordedEvent)
        {
            ResetManualEditor();
            return;
        }

        PopulateManualEditor(recordedEvent);
        if (RecordedVideo.Source is null)
        {
            return;
        }

        var position = recordedEvent.Offset;
        if (
            RecordedVideo.NaturalDuration.HasTimeSpan
            && position > RecordedVideo.NaturalDuration.TimeSpan
        )
        {
            position = RecordedVideo.NaturalDuration.TimeSpan;
            SetManualEditorMessage(
                $"이 이벤트는 영상 길이 밖({recordedEvent.Offset.TotalSeconds:0.000}초)에 있어 재생 끝으로 이동했습니다."
            );
        }

        RecordedVideo.Position = position;
        UpdateVideoTimeText();
        RenderEventOverlay(position);
    }

    private void ApplyEventPolicies()
    {
        var videoDuration = RecordedVideo.NaturalDuration.HasTimeSpan
            ? RecordedVideo.NaturalDuration.TimeSpan
            : (TimeSpan?)null;
        foreach (var recordedEvent in RecordedEvents)
        {
            ApplyEventPolicy(recordedEvent, videoDuration);
        }
    }

    private void ClearExecutionResults()
    {
        foreach (var recordedEvent in RecordedEvents)
        {
            recordedEvent.LastExecutionResult = null;
            recordedEvent.LastExecutionFailed = false;
        }
    }

    private void ApplyEventPolicy(
        RecordedEvent recordedEvent,
        TimeSpan? videoDuration = null
    )
    {
        MigrateLegacyReviewOnlyQuarantine(recordedEvent);

        var technicalBlockReason = MacroEventPolicy.GetTechnicalBlockReason(
            recordedEvent
        );
        if (technicalBlockReason is not null)
        {
            recordedEvent.IsQuarantined = true;
            recordedEvent.QuarantineReason = technicalBlockReason;
        }
        else if (
            recordedEvent.IsQuarantined
            && string.IsNullOrWhiteSpace(recordedEvent.QuarantineReason)
        )
        {
            recordedEvent.QuarantineReason =
                "현재 엔진이 기술적으로 실행할 수 없는 이벤트입니다.";
        }

        recordedEvent.ReviewWarningText = null;
    }

    private static void MigrateLegacyReviewOnlyQuarantine(
        RecordedEvent recordedEvent
    )
    {
        if (
            !recordedEvent.IsQuarantined
            || !MacroEventPolicy.IsLegacyReviewOnlyQuarantine(
                recordedEvent.QuarantineReason
            )
        )
        {
            return;
        }

        recordedEvent.ReviewWarningText = recordedEvent.QuarantineReason;
        recordedEvent.QuarantineReason = null;
        recordedEvent.IsQuarantined = false;
    }

    private void RenderEventOverlay(TimeSpan videoPosition)
    {
        EventOverlayCanvas.Children.Clear();
        if (
            RecordedVideo.Source is null
            || EventOverlayCanvas.ActualWidth <= 0
            || EventOverlayCanvas.ActualHeight <= 0
        )
        {
            return;
        }

        var activeEvents = RecordedEvents
            .Where(
                recordedEvent =>
                    !string.IsNullOrWhiteSpace(
                        recordedEvent.OverlayLabel ?? recordedEvent.Message
                    )
                    && videoPosition >= recordedEvent.Offset
                    && videoPosition - recordedEvent.Offset <= EventOverlayDuration
            )
            .OrderBy(recordedEvent => recordedEvent.Offset)
            .ThenBy(recordedEvent => recordedEvent.Sequence)
            .TakeLast(4)
            .ToList();

        for (var index = 0; index < activeEvents.Count; index++)
        {
            var recordedEvent = activeEvents[index];
            var age = videoPosition - recordedEvent.Offset;
            var progress = Math.Clamp(
                age.TotalMilliseconds / EventOverlayDuration.TotalMilliseconds,
                0,
                1
            );
            AddEventOverlay(recordedEvent, index, progress);
        }
    }

    private void AddEventOverlay(RecordedEvent recordedEvent, int index, double progress)
    {
        var eventBrush = recordedEvent.Category switch
        {
            "키보드" => KeyboardEventBrush,
            "수동" => ManualEventBrush,
            _ => MouseEventBrush,
        };
        if (recordedEvent.IsQuarantined)
        {
            eventBrush = OutsideEventBrush;
        }
        else if (recordedEvent.HasReviewWarning)
        {
            eventBrush = ReviewWarningBrush;
        }

        var baseLabel = recordedEvent.OverlayLabel ?? recordedEvent.Message;
        var label = recordedEvent.IsQuarantined
            ? $"실패 · {baseLabel}"
            : recordedEvent.HasReviewWarning
                ? $"확인 · {baseLabel}"
                : baseLabel;

        if (
            recordedEvent.ScreenX is not double screenX
            || recordedEvent.ScreenY is not double screenY
            || recordedEvent.CaptureWidth <= 0
            || recordedEvent.CaptureHeight <= 0
        )
        {
            AddOverlayBanner(label, eventBrush, index, progress);
            return;
        }

        var relativeX =
            (screenX - recordedEvent.CaptureLeft) / recordedEvent.CaptureWidth;
        var relativeY =
            (screenY - recordedEvent.CaptureTop) / recordedEvent.CaptureHeight;
        if (relativeX < 0 || relativeX > 1 || relativeY < 0 || relativeY > 1)
        {
            AddOverlayBanner($"{label} · 녹화 화면 밖", OutsideEventBrush, index, progress);
            return;
        }

        var naturalWidth = RecordedVideo.NaturalVideoWidth;
        var naturalHeight = RecordedVideo.NaturalVideoHeight;
        if (naturalWidth <= 0 || naturalHeight <= 0)
        {
            return;
        }

        var canvasWidth = EventOverlayCanvas.ActualWidth;
        var canvasHeight = EventOverlayCanvas.ActualHeight;
        var videoScale = Math.Min(
            canvasWidth / naturalWidth,
            canvasHeight / naturalHeight
        );
        var displayedWidth = naturalWidth * videoScale;
        var displayedHeight = naturalHeight * videoScale;
        var videoLeft = (canvasWidth - displayedWidth) / 2;
        var videoTop = (canvasHeight - displayedHeight) / 2;
        var x = videoLeft + relativeX * displayedWidth;
        var y = videoTop + relativeY * displayedHeight;
        var opacity = Math.Max(0.38, 1 - progress * 0.62);

        if (
            recordedEvent.ActionKind == MacroActionKind.MouseDrag
            && recordedEvent.MousePath.Length > 1
        )
        {
            var pathPoints = new PointCollection(
                recordedEvent.MousePath.Select(point =>
                    new Point(
                        videoLeft
                            + (double)(point.X - recordedEvent.CaptureLeft)
                                / recordedEvent.CaptureWidth
                                * displayedWidth,
                        videoTop
                            + (double)(point.Y - recordedEvent.CaptureTop)
                                / recordedEvent.CaptureHeight
                                * displayedHeight
                    )
                )
            );
            EventOverlayCanvas.Children.Add(
                new System.Windows.Shapes.Polyline
                {
                    Points = pathPoints,
                    Stroke = eventBrush,
                    StrokeThickness = 5,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Opacity = opacity,
                }
            );
        }

        var ringSize = 54 + progress * 34;
        var ring = new Ellipse
        {
            Width = ringSize,
            Height = ringSize,
            Stroke = eventBrush,
            StrokeThickness = 4,
            Opacity = opacity,
        };
        Canvas.SetLeft(ring, x - ringSize / 2);
        Canvas.SetTop(ring, y - ringSize / 2);
        EventOverlayCanvas.Children.Add(ring);

        var center = new Ellipse
        {
            Width = 14,
            Height = 14,
            Fill = eventBrush,
            Stroke = Brushes.White,
            StrokeThickness = 3,
            Opacity = opacity,
        };
        Canvas.SetLeft(center, x - 7);
        Canvas.SetTop(center, y - 7);
        EventOverlayCanvas.Children.Add(center);

        var labelBorder = CreateOverlayLabel(label, eventBrush, opacity);
        var labelLeft = Math.Clamp(x + 24, 12, Math.Max(12, canvasWidth - 230));
        var labelTop = Math.Clamp(
            y - 48 - index * 34,
            12,
            Math.Max(12, canvasHeight - 52)
        );
        Canvas.SetLeft(labelBorder, labelLeft);
        Canvas.SetTop(labelBorder, labelTop);
        EventOverlayCanvas.Children.Add(labelBorder);
    }

    private void AddOverlayBanner(
        string label,
        SolidColorBrush eventBrush,
        int index,
        double progress
    )
    {
        var banner = CreateOverlayLabel(
            label,
            eventBrush,
            Math.Max(0.38, 1 - progress * 0.62)
        );
        Canvas.SetLeft(banner, 16);
        Canvas.SetTop(banner, 16 + index * 46);
        EventOverlayCanvas.Children.Add(banner);
    }

    private static Border CreateOverlayLabel(
        string label,
        SolidColorBrush eventBrush,
        double opacity
    )
    {
        return new Border
        {
            MaxWidth = 220,
            Padding = new Thickness(11, 7, 11, 7),
            Background = new SolidColorBrush(Color.FromArgb(224, 17, 24, 39)),
            BorderBrush = eventBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Opacity = opacity,
            Child = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            },
        };
    }

    private void RefreshCaptureBounds()
    {
        captureLeft = 0;
        captureTop = 0;
        captureWidth = Math.Max(1, GetSystemMetrics(SmCxScreen));
        captureHeight = Math.Max(1, GetSystemMetrics(SmCyScreen));
    }

    private bool IsPointInsideCapture(int x, int y)
    {
        return x >= captureLeft
            && x < captureLeft + captureWidth
            && y >= captureTop
            && y < captureTop + captureHeight;
    }

    private static bool GetCursorPosition(out int x, out int y)
    {
        if (GetCursorPos(out var point))
        {
            x = point.X;
            y = point.Y;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    private static bool IsOwnProcessWindowAt(int x, int y)
    {
        var window = WindowFromPoint(new NativePoint { X = x, Y = y });
        return IsOwnProcessWindow(window);
    }

    private bool IsForegroundInputInsideCapture()
    {
        var foregroundWindow = GetForegroundWindow();
        if (
            !TryGetExternalInputWindow(
                foregroundWindow,
                out var window,
                out _
            )
            || !GetWindowRect(window, out var bounds)
        )
        {
            return false;
        }

        return bounds.Right > captureLeft
            && bounds.Left < captureLeft + captureWidth
            && bounds.Bottom > captureTop
            && bounds.Top < captureTop + captureHeight;
    }

    private void RememberMacroTargetWindow()
    {
        var targetWindow = GetRootWindow(GetForegroundWindow());
        if (
            (
                targetWindow == IntPtr.Zero
                || !IsWindow(targetWindow)
                || IsOwnProcessWindow(targetWindow)
            )
            && GetCursorPos(out var cursorPoint)
        )
        {
            targetWindow = GetRootWindow(WindowFromPoint(cursorPoint));
        }

        if (
            targetWindow != IntPtr.Zero
            && IsWindow(targetWindow)
            && !IsOwnProcessWindow(targetWindow)
        )
        {
            macroTargetWindow = targetWindow;
        }
    }

    private bool TryRestoreMacroTargetWindow(out string error)
    {
        if (
            macroTargetWindow == IntPtr.Zero
            || !IsWindow(macroTargetWindow)
        )
        {
            error =
                "실행할 업무 창을 찾지 못했습니다. 실행 지연을 3초 이상으로 두고 대상 창을 선택하세요.";
            return false;
        }

        if (
            !TryGetExternalInputWindow(
                macroTargetWindow,
                out var targetWindow,
                out error
            )
        )
        {
            return false;
        }

        var currentWindow = GetRootWindow(GetForegroundWindow());
        if (currentWindow == targetWindow)
        {
            return true;
        }

        _ = SetForegroundWindow(targetWindow);
        Thread.Sleep(80);
        currentWindow = GetRootWindow(GetForegroundWindow());
        if (currentWindow != targetWindow)
        {
            error =
                "선택한 업무 창을 다시 활성화하지 못했습니다. 대상 창을 직접 선택한 뒤 다시 실행하세요.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool TryValidateForegroundInput(out string error)
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            error = "키보드 입력 대상 창을 찾지 못했습니다.";
            return false;
        }

        var window = GetRootWindow(foregroundWindow);
        if (
            IsOwnProcessWindow(window)
            && isMacroRunning
            && macroTargetWindow != IntPtr.Zero
        )
        {
            return TryRestoreMacroTargetWindow(out error);
        }

        if (!TryGetExternalInputWindow(window, out window, out error))
        {
            return false;
        }

        if (isMacroCountingDown || isMacroRunning)
        {
            macroTargetWindow = window;
        }
        return true;
    }

    private bool TryGetExternalInputWindow(
        IntPtr candidateWindow,
        out IntPtr window,
        out string error
    )
    {
        window = GetRootWindow(candidateWindow);
        if (window == IntPtr.Zero || !IsWindow(window))
        {
            error = "키보드 입력 대상 창을 찾지 못했습니다.";
            return false;
        }

        if (IsOwnProcessWindow(window))
        {
            error = "매크로 앱 자체에는 키보드 입력을 보낼 수 없습니다.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static IntPtr GetRootWindow(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var rootWindow = GetAncestor(window, GetAncestorRoot);
        return rootWindow == IntPtr.Zero
            ? window
            : rootWindow;
    }

    private static bool IsOwnProcessWindow(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return false;
        }

        window = GetRootWindow(window);

        _ = GetWindowThreadProcessId(window, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    private void UpdateVideoTimeText()
    {
        var current = RecordedVideo.Position;
        var total = RecordedVideo.NaturalDuration.HasTimeSpan
            ? RecordedVideo.NaturalDuration.TimeSpan
            : TimeSpan.Zero;
        VideoTimeText.Text =
            $"{(int)current.TotalMinutes:00}:{current.Seconds:00} / "
            + $"{(int)total.TotalMinutes:00}:{total.Seconds:00}";
    }

    private async Task CompleteAndDrainHookEventQueueAsync()
    {
        var queue = hookEventQueue;
        hookEventQueue = null;
        if (queue is not null)
        {
            await queue.CompleteAndDrainAsync();
        }
    }

    private void CompleteHookEventQueue()
    {
        var queue = hookEventQueue;
        hookEventQueue = null;
        queue?.Complete();
    }

    private async Task StopGlobalHookAsync()
    {
        var hook = globalHook;
        var runTask = globalHookRunTask;
        var readySource = globalHookReadySource;
        var hookEnabledHandler = globalHookEnabledHandler;
        var stopRequest = globalHookStopRequest;
        stopRequest?.Cancel();
        globalHook = null;
        globalHookRunTask = null;
        if (hook is null)
        {
            ClearGlobalHookStartupState(
                readySource,
                hookEnabledHandler,
                stopRequest
            );
            return;
        }

        var errors = new List<Exception>();
        if (
            runTask is not null
            && readySource is not null
            && !readySource.Task.IsCompleted
            && !runTask.IsCompleted
        )
        {
            try
            {
                await Task.WhenAny(readySource.Task, runTask)
                    .WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        }

        try
        {
            hook.Stop();
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }

        if (runTask is not null)
        {
            try
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        }

        try
        {
            hook.Dispose();
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }

        if (runTask is not null && !runTask.IsCompleted)
        {
            try
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        }

        try
        {
            DetachGlobalHookHandlers(hook, hookEnabledHandler);
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }
        finally
        {
            ClearGlobalHookStartupState(
                readySource,
                hookEnabledHandler,
                stopRequest
            );
        }

        if (errors.Count > 0)
        {
            throw new AggregateException(
                "입력 이벤트 감지를 완전히 종료하지 못했습니다.",
                errors
            );
        }
    }

    private void StopGlobalHook()
    {
        var hook = globalHook;
        var readySource = globalHookReadySource;
        var hookEnabledHandler = globalHookEnabledHandler;
        var stopRequest = globalHookStopRequest;
        stopRequest?.Cancel();
        globalHook = null;
        globalHookRunTask = null;
        if (hook is null)
        {
            ClearGlobalHookStartupState(
                readySource,
                hookEnabledHandler,
                stopRequest
            );
            return;
        }

        try
        {
            hook.Stop();
        }
        catch
        {
            // Failure cleanup continues even if the native hook already stopped.
        }
        finally
        {
            try
            {
                hook.Dispose();
            }
            finally
            {
                try
                {
                    DetachGlobalHookHandlers(hook, hookEnabledHandler);
                }
                finally
                {
                    ClearGlobalHookStartupState(
                        readySource,
                        hookEnabledHandler,
                        stopRequest
                    );
                }
            }
        }
    }

    private void DetachGlobalHookHandlers(
        SimpleGlobalHook hook,
        EventHandler<HookEventArgs>? hookEnabledHandler
    )
    {
        if (hookEnabledHandler is not null)
        {
            hook.HookEnabled -= hookEnabledHandler;
        }
        hook.MousePressed -= GlobalHook_MousePressed;
        hook.MouseReleased -= GlobalHook_MouseReleased;
        hook.MouseDragged -= GlobalHook_MouseDragged;
        hook.MouseWheel -= GlobalHook_MouseWheel;
        hook.KeyPressed -= GlobalHook_KeyPressed;
        hook.KeyReleased -= GlobalHook_KeyReleased;
    }

    private void ClearGlobalHookStartupState(
        TaskCompletionSource<bool>? readySource,
        EventHandler<HookEventArgs>? hookEnabledHandler,
        CancellationTokenSource? stopRequest
    )
    {
        if (ReferenceEquals(globalHookReadySource, readySource))
        {
            globalHookReadySource = null;
        }
        if (ReferenceEquals(globalHookEnabledHandler, hookEnabledHandler))
        {
            globalHookEnabledHandler = null;
        }
        if (ReferenceEquals(globalHookStopRequest, stopRequest))
        {
            globalHookStopRequest = null;
        }
        stopRequest?.Dispose();
    }

    private void DisposeRecorder()
    {
        var activeRecorder = recorder;
        recorder = null;
        if (activeRecorder is null)
        {
            return;
        }

        activeRecorder.OnRecordingComplete -= Recorder_OnRecordingComplete;
        activeRecorder.OnRecordingFailed -= Recorder_OnRecordingFailed;
        activeRecorder.OnStatusChanged -= Recorder_OnStatusChanged;
        activeRecorder.Dispose();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        windowSource = HwndSource.FromHwnd(windowHandle);
        windowSource?.AddHook(WindowMessageHook);
        emergencyHotkeyRegistered = RegisterHotKey(
            windowHandle,
            EmergencyHotkeyId,
            ModControl | ModShift,
            VkF12
        );
        EmergencyStopText.Text = emergencyHotkeyRegistered
            ? "긴급 중지: Ctrl + Shift + F12"
            : "긴급 단축키 등록 실패 · 실행 중지 버튼을 사용하세요";
        EmergencyStopText.Foreground = emergencyHotkeyRegistered
            ? new SolidColorBrush(Color.FromRgb(152, 162, 179))
            : new SolidColorBrush(Color.FromRgb(180, 35, 24));
    }

    private IntPtr WindowMessageHook(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled
    )
    {
        if (
            message == WmHotkey
            && wordParameter.ToInt32() == EmergencyHotkeyId
        )
        {
            handled = true;
            HandleEmergencyStop();
        }

        return IntPtr.Zero;
    }

    private void HandleEmergencyStop()
    {
        if (isMacroCountingDown)
        {
            CancelMacroCountdown();
            return;
        }

        if (isMacroRunning)
        {
            RecordingStatusText.Text = "긴급 중지 단축키를 감지했습니다…";
            macroCancellation?.Cancel();
            return;
        }

        if (isCountingDown)
        {
            CancelRecordingCountdown();
            return;
        }

        if (isRecording || isPreparingRecording)
        {
            StopRecording();
        }
    }

    private async void MainWindow_Closing(
        object? sender,
        System.ComponentModel.CancelEventArgs e
    )
    {
        if (!closeAfterSave)
        {
            e.Cancel = true;
            closeRequested = true;
            countdownTimer.Stop();
            isCountingDown = false;
            macroCountdownTimer.Stop();
            isMacroCountingDown = false;
            macroCancellation?.Cancel();
            projectSaveTimer.Stop();

            if (isRecording || isPreparingRecording || isFinalizing)
            {
                RecordingStatusText.Text =
                    "종료 전에 녹화 영상을 안전하게 저장하고 있습니다…";
                if (!isFinalizing)
                {
                    StopRecording();
                }
                return;
            }

            await FinishCloseRequestAsync();
            return;
        }

        CleanupForClose();
    }

    private async Task FinishCloseRequestAsync()
    {
        if (
            !closeRequested
            || closeAfterSave
            || isCompletingCloseRequest
            || isRecording
            || isPreparingRecording
            || isFinalizing
        )
        {
            return;
        }

        isCompletingCloseRequest = true;
        try
        {
            var executionTask = macroExecutionTask;
            if (executionTask is not null && !executionTask.IsCompleted)
            {
                RecordingStatusText.Text =
                    "실행을 중지하고 현재 입력을 해제한 뒤 종료합니다…";
                await executionTask;
            }

            projectSaveTimer.Stop();
            await SaveCurrentProjectAsync();

            closeAfterSave = true;
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(Close)
            );
        }
        finally
        {
            if (!closeAfterSave)
            {
                isCompletingCloseRequest = false;
            }
        }
    }

    private void CleanupForClose()
    {
        countdownTimer.Stop();
        macroCountdownTimer.Stop();
        recordingTimer.Stop();
        videoTimer.Stop();
        projectSaveTimer.Stop();
        macroCancellation?.Cancel();
        StopGlobalHook();
        CompleteHookEventQueue();
        DisposeRecorder();
        if (windowSource is not null)
        {
            if (emergencyHotkeyRegistered)
            {
                UnregisterHotKey(windowSource.Handle, EmergencyHotkeyId);
                emergencyHotkeyRegistered = false;
            }
            windowSource.RemoveHook(WindowMessageHook);
            windowSource = null;
        }
    }

    private sealed class MacroInputCleanupException(
        string message,
        Exception innerException
    ) : Exception(message, innerException);

    private sealed record HookEventSnapshot(
        long SessionId,
        TimeSpan Offset,
        string Category,
        string Message,
        double? ScreenX,
        double? ScreenY,
        string? OverlayLabel,
        MacroActionKind ActionKind,
        string? ActionText,
        KeyCode[] KeyCodes,
        int WheelRotation,
        bool IsHorizontalWheel,
        KeyCode[] ModifierKeyCodes,
        bool IsQuarantined,
        string? QuarantineReason,
        string? ReviewWarningText,
        int CaptureLeft,
        int CaptureTop,
        int CaptureWidth,
        int CaptureHeight,
        double? EndScreenX,
        double? EndScreenY,
        MouseButton? DragButton,
        TimeSpan? DragDuration,
        MousePathPoint[] MousePath
    );

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private sealed class PendingMousePress(
        long sessionId,
        TimeSpan offset,
        MouseButton button,
        MacroActionKind actionKind,
        int startX,
        int startY,
        KeyCode[] modifierKeyCodes
    )
    {
        private readonly List<MousePathPoint> path =
        [
            new MousePathPoint(TimeSpan.Zero, startX, startY),
        ];

        public long SessionId { get; } = sessionId;

        public TimeSpan Offset { get; } = offset;

        public MouseButton Button { get; } = button;

        public MacroActionKind ActionKind { get; } = actionKind;

        public int StartX { get; } = startX;

        public int StartY { get; } = startY;

        public KeyCode[] ModifierKeyCodes { get; } = modifierKeyCodes;

        public bool WasDragged { get; private set; }

        public void MarkDragged(int x, int y, TimeSpan eventOffset)
        {
            if (
                Math.Abs(x - StartX) >= 2
                || Math.Abs(y - StartY) >= 2
            )
            {
                WasDragged = true;
            }

            if (!WasDragged)
            {
                return;
            }

            AddPathPoint(x, y, eventOffset);
        }

        public void Complete(
            int x,
            int y,
            TimeSpan eventOffset,
            bool forceDrag
        )
        {
            if (forceDrag || WasDragged || path.Count > 1)
            {
                AddPathPoint(x, y, eventOffset, force: true);
            }
        }

        public MousePathPoint[] GetPath() => [.. path];

        private void AddPathPoint(
            int x,
            int y,
            TimeSpan eventOffset,
            bool force = false
        )
        {
            var last = path[^1];
            var relativeOffset = eventOffset > Offset
                ? eventOffset - Offset
                : TimeSpan.Zero;
            var distance = Math.Sqrt(
                Math.Pow(x - last.X, 2) + Math.Pow(y - last.Y, 2)
            );
            if (!force && distance < 2)
            {
                return;
            }
            if (
                last.X == x
                && last.Y == y
                && relativeOffset <= last.Offset
            )
            {
                return;
            }

            path.Add(new MousePathPoint(relativeOffset, x, y));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeInputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)]
        public NativeMouseInput Mouse;

        [FieldOffset(0)]
        public NativeKeyboardInput Keyboard;

        [FieldOffset(0)]
        public NativeHardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeHardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId
    );

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint numberOfInputs,
        [In] NativeInput[] inputs,
        int sizeOfInput
    );

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr windowHandle,
        int id,
        uint modifiers,
        uint virtualKey
    );

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
