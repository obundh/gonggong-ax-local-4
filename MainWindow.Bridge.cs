using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Series4.Desktop;

public partial class MainWindow
{
    internal static uint BridgeParentPid;
    private CancellationTokenSource? bridgeRun;
    private int bridgeIteration;
    private int bridgeTotal;
    private StreamWriter? bridgeOutput;
    private bool BridgeBusy => isRecording || isPreparingRecording || isFinalizing || isCountingDown || isMacroRunning || isMacroCountingDown || bridgeRun is not null;

    internal void StartBridge(string[] args)
    {
        var parent = Array.IndexOf(args, "--parent-pid");
        if (parent >= 0 && parent + 1 < args.Length) uint.TryParse(args[parent + 1], out BridgeParentPid);
        new WindowInteropHelper(this).EnsureHandle();
        isStartupLoading = false;
        bridgeOutput = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        RecordingStatusText.Text = "준비 완료";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) => BridgeWrite(new { type = "state", state = BridgeState() });
        timer.Start();
        _ = Task.Run(async () =>
        {
            using var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            string? line;
            while ((line = await input.ReadLineAsync()) is not null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var command = doc.RootElement.Clone();
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        try { await BridgeCommand(command); BridgeWrite(new { id = command.GetProperty("id").GetInt32(), ok = true }); }
                        catch (Exception error) { BridgeWrite(new { id = command.GetProperty("id").GetInt32(), ok = false, error = error.Message }); }
                    }).Task.Unwrap();
                }
                catch (Exception error) { BridgeWrite(new { type = "error", error = error.Message }); }
            }
            await Dispatcher.InvokeAsync(async () => { HandleEmergencyStop(); await StopRecordingAsync(); await SaveCurrentProjectAsync(); Application.Current.Shutdown(); });
        });
    }

    private void BridgeWrite(object value)
    {
        if (bridgeOutput is null) return;
        lock (bridgeOutput) { try { bridgeOutput.WriteLine(JsonSerializer.Serialize(value)); } catch (IOException) { } }
    }

    private object BridgeState() => new
    {
        busy = BridgeBusy,
        phase = isCountingDown || isMacroCountingDown ? "countdown" : isPreparingRecording ? "preparing" : isRecording ? "recording" : isFinalizing ? "saving" : bridgeRun is not null || isMacroRunning ? "running" : "idle",
        message = RecordingStatusText.Text, timer = RecordingTimerText.Text,
        videoPath = !isRecording && !isFinalizing && !isPreparingRecording ? currentVideoPath : null,
        projectPath = currentProjectPath, iteration = bridgeIteration, total = bridgeTotal,
        width = captureWidth, height = captureHeight,
        events = RecordedEvents.Select((item, index) => new { id = index, name = item.Category, detail = item.Message, at = item.Offset.TotalSeconds, result = item.LastExecutionResult, failed = item.LastExecutionFailed, executable = item.IsExecutable, x = item.ScreenX, y = item.ScreenY, text = item.ActionText, kind = item.ActionKind.ToString(), label = item.OverlayLabel, captureLeft = item.CaptureLeft, captureTop = item.CaptureTop, captureWidth = item.CaptureWidth, captureHeight = item.CaptureHeight }).ToArray()
    };

    private async Task BridgeCommand(JsonElement command)
    {
        var action = command.GetProperty("action").GetString();
        if (action == "stop") { HandleEmergencyStop(); return; }
        if (action == "state") { BridgeWrite(new { type = "state", state = BridgeState() }); return; }
        if (BridgeBusy) throw new InvalidOperationException("진행 중인 작업을 먼저 중지하세요.");
        switch (action)
        {
            case "add":
                if (currentVideoPath is null) throw new InvalidOperationException("먼저 영상이나 기록을 여세요.");
                var addAt = command.GetProperty("at").GetDouble();
                if (!double.IsFinite(addAt) || addAt < 0 || addAt > 86400) throw new ArgumentException("시간 범위: 0~86400초");
                var kind = command.GetProperty("kind").GetString();
                var added = new RecordedEvent {
                    Offset = TimeSpan.FromSeconds(addAt), Category = "", Message = "",
                    Sequence = RecordedEvents.Count == 0 ? 1 : RecordedEvents.Max(e => e.Sequence) + 1,
                    CaptureLeft = captureLeft, CaptureTop = captureTop, CaptureWidth = captureWidth, CaptureHeight = captureHeight
                };
                if (kind == "Wait") {
                    var duration = command.GetProperty("seconds").GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (!MacroEventPolicy.TryGetWaitSeconds(duration, out _)) throw new ArgumentException("대기 시간: 0.1~3600초");
                    added.ActionKind = MacroActionKind.Wait; added.ActionText = duration;
                    added.Category = "대기"; added.Message = duration + "초 대기"; added.OverlayLabel = added.Message;
                } else if (kind == "TextEntry") {
                    var input = command.GetProperty("text").GetString();
                    if (string.IsNullOrEmpty(input) || input.Length > 10000) throw new ArgumentException("텍스트 길이: 1~10000자");
                    added.ActionKind = MacroActionKind.TextEntry; added.ActionText = input;
                    added.Category = "텍스트 입력"; added.Message = input; added.OverlayLabel = "텍스트 입력";
                } else if (kind is "MouseLeftClick" or "MouseRightClick") {
                    added.ActionKind = kind == "MouseLeftClick" ? MacroActionKind.MouseLeftClick : MacroActionKind.MouseRightClick;
                    added.ScreenX = command.GetProperty("x").GetDouble(); added.ScreenY = command.GetProperty("y").GetDouble();
                    added.Category = kind == "MouseLeftClick" ? "왼쪽 클릭" : "오른쪽 클릭";
                    added.Message = $"좌표 {added.ScreenX}, {added.ScreenY}"; added.OverlayLabel = added.Category;
                } else throw new ArgumentException("지원하지 않는 추가 동작입니다.");
                var blockReason = MacroEventPolicy.GetTechnicalBlockReason(added);
                if (blockReason is not null) throw new ArgumentException(blockReason);
                var insertIndex = 0;
                while (insertIndex < RecordedEvents.Count && RecordedEvents[insertIndex].Offset <= added.Offset) insertIndex++;
                RecordedEvents.Insert(insertIndex, added);
                ApplyEventPolicies(); ScheduleProjectSave(); UpdateEventLogUi();
                await SaveCurrentProjectAsync(propagateFailure: true);
                RecordingStatusText.Text = "행동 추가 · 저장 완료";
                break;
            case "record":
                if (!emergencyHotkeyRegistered) throw new InvalidOperationException("긴급 정지 단축키 등록 실패: 다른 매크로 앱을 종료한 후 다시 실행하세요.");
                StartRecordingButton_Click(this, new RoutedEventArgs());
                break;
            case "run":
                if (!emergencyHotkeyRegistered) throw new InvalidOperationException("긴급 정지 단축키 등록 실패: 다른 매크로 앱을 종료한 후 다시 실행하세요.");
                if (!RecordedEvents.Any(item => item.IsExecutable)) throw new InvalidOperationException("실행할 행동 기록이 없습니다.");
                bridgeTotal = Math.Clamp(command.GetProperty("repeats").GetInt32(), 1, 999);
                bridgeRun = new CancellationTokenSource();
                _ = BridgeRepeatAsync(bridgeRun);
                break;
            case "open":
                await SaveCurrentProjectAsync(propagateFailure: true);
                var path = command.GetProperty("path").GetString()!;
                var sidecar = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? path : MacroProjectStore.GetSidecarPath(path);
                var loaded = File.Exists(sidecar) ? await MacroProjectStore.LoadAsync(sidecar) : null;
                RecordedEvents.Clear();
                if (loaded is not null) foreach (var item in loaded.RecordedEvents) RecordedEvents.Add(item);
                currentProjectPath = loaded?.ProjectPath;
                currentProjectStatus = loaded?.Status ?? MacroProjectStatus.Completed;
                projectRevision = 0; lastCommittedProjectRevision = 0;
                LoadRecordedVideo(loaded?.CurrentVideoPath ?? path);
                ApplyEventPolicies(); UpdateEventLogUi();
                RecordingStatusText.Text = loaded is null ? "영상 열림 · 행동 기록 없음" : $"{RecordedEvents.Count}개 행동 불러옴";
                break;
            case "save":
                if (currentVideoPath is null) throw new InvalidOperationException("저장할 기록이 없습니다.");
                projectRevision++;
                await SaveCurrentProjectAsync(propagateFailure: true);
                RecordingStatusText.Text = "저장 완료";
                break;
            case "delete":
                RecordedEvents.RemoveAt(command.GetProperty("index").GetInt32());
                ScheduleProjectSave(); UpdateEventLogUi();
                break;
            case "edit":
                var itemToEdit = RecordedEvents[command.GetProperty("index").GetInt32()];
                var at = command.GetProperty("at").GetDouble();
                if (!double.IsFinite(at) || at < 0 || at > 86400) throw new ArgumentException("시간 범위: 0~86400초");
                if (itemToEdit.ActionKind == MacroActionKind.Wait && command.TryGetProperty("seconds", out var waitValue)) {
                    var duration = waitValue.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (!MacroEventPolicy.TryGetWaitSeconds(duration, out _)) throw new ArgumentException("대기 시간: 0.1~3600초");
                    itemToEdit.ActionText = duration; itemToEdit.Message = duration + "초 대기"; itemToEdit.OverlayLabel = itemToEdit.Message;
                }
                itemToEdit.Offset = TimeSpan.FromSeconds(at);
                if (command.TryGetProperty("x", out var x) && x.ValueKind == JsonValueKind.Number) itemToEdit.ScreenX = x.GetDouble();
                if (command.TryGetProperty("y", out var y) && y.ValueKind == JsonValueKind.Number) itemToEdit.ScreenY = y.GetDouble();
                if (itemToEdit.ActionKind == MacroActionKind.TextEntry && command.TryGetProperty("text", out var text)) {
                    var input = text.GetString();
                    if (string.IsNullOrEmpty(input) || input.Length > 10000) throw new ArgumentException("텍스트 길이: 1~10000자");
                    itemToEdit.ActionText = input; itemToEdit.Message = input;
                }
                ApplyEventPolicies(); ScheduleProjectSave(); UpdateEventLogUi();
                break;
            default: throw new ArgumentException("지원하지 않는 명령입니다.");
        }
    }

    private async Task BridgeRepeatAsync(CancellationTokenSource cancellation)
    {
        try
        {
            for (bridgeIteration = 1; bridgeIteration <= bridgeTotal; bridgeIteration++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                await RunMacroAsync();
                if (cancellation.IsCancellationRequested) { HandleEmergencyStop(); cancellation.Token.ThrowIfCancellationRequested(); }
                while (isMacroCountingDown || isMacroRunning) await Task.Delay(100, cancellation.Token);
                if (macroExecutionTask is not null) await macroExecutionTask;
                cancellation.Token.ThrowIfCancellationRequested();
                var log = new { at = DateTimeOffset.Now, iteration = bridgeIteration, total = bridgeTotal, message = RecordingStatusText.Text, events = RecordedEvents.Select(item => new { item.Sequence, item.LastExecutionResult, item.LastExecutionFailed }) };
                if (currentVideoPath is not null) await File.AppendAllTextAsync(currentVideoPath + ".runs.jsonl", JsonSerializer.Serialize(log) + Environment.NewLine);
                if (RecordedEvents.Any(item => item.LastExecutionFailed)) break;
            }
            bridgeIteration = Math.Min(bridgeIteration, bridgeTotal);
        }
        catch (OperationCanceledException) { macroCancellation?.Cancel(); RecordingStatusText.Text = "반복 중지"; }
        catch (Exception error) { RecordingStatusText.Text = $"실행 오류: {error.Message}"; }
        finally { bridgeRun = null; cancellation.Dispose(); }
    }
}
