using System.Windows;

namespace Series4.Desktop;

public partial class App : Application
{
    private Mutex? singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\GonggongAX.Series4.Desktop",
            createdNew: out var createdNew
        );
        if (!createdNew)
        {
            singleInstanceMutex.Dispose();
            singleInstanceMutex = null;
            MessageBox.Show(
                "공공 AX 업무 매크로가 이미 실행 중입니다.\n기존 창을 사용해 주세요.",
                "공공 AX 업무 매크로",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (singleInstanceMutex is not null)
        {
            try
            {
                singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The process no longer owns the mutex; disposal is sufficient.
            }
            singleInstanceMutex.Dispose();
            singleInstanceMutex = null;
        }

        base.OnExit(e);
    }
}
