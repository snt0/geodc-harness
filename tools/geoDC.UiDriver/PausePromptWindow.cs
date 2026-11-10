using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace geoDC.UiDriver;

/// <summary>
///     The <see cref="IPausePrompt" /> a developer sees: a small topmost window owned by the UI driver process, not by
///     geoDC, naming the step and what to check, with a note box and Continue and Fail buttons. It runs on a thread
///     of its own with its own dispatcher, so geoDC stays fully usable while the UI driver waits. Closing the window
///     counts as Fail, and the request's timeout closes it as <see cref="PauseVerdict.TimedOut" />.
/// </summary>
public sealed class PausePromptWindow : IPausePrompt
{
    /// <inheritdoc />
    /// <exception cref="DriverException"><c>pause-failed</c> (exit code 1) when the window could not be shown.</exception>
    public PauseAnswer Ask(PauseRequest request)
    {
        PauseAnswer? answer = null;
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                answer = Show(request);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            Console.Error.WriteLine($"geodc-ui: the pause window failed: {failure}");
            throw new DriverException("pause-failed", $"Could not show the pause window: {failure.Message}", 1);
        }

        return answer ?? new PauseAnswer(PauseVerdict.Failed, null, 0);
    }

    private static PauseAnswer Show(PauseRequest request)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        PauseVerdict verdict = PauseVerdict.Failed;

        TextBlock heading = new()
        {
            Text = request.Location is null
                ? $"Paused at step {request.Step}"
                : $"Paused at step {request.Step} · {request.Location}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        TextBlock message = new()
        {
            Text = request.Message ?? "Check the app, then continue or fail the check.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        TextBlock noteLabel = new() { Text = "Note (optional)", Margin = new Thickness(0, 0, 0, 4) };
        TextBox note = new()
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 56,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Button fail = new() { Content = "_Fail", MinWidth = 88, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        Button proceed = new() { Content = "_Continue", MinWidth = 88, IsDefault = true };
        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { fail, proceed }
        };
        StackPanel body = new() { Margin = new Thickness(16), Children = { heading, message, noteLabel, note, buttons } };

        Window window = new()
        {
            Title = "geoDC UI driver: manual check",
            Content = body,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
            ShowInTaskbar = true
        };

        proceed.Click += (_, _) =>
        {
            verdict = PauseVerdict.Continued;
            window.Close();
        };
        fail.Click += (_, _) =>
        {
            verdict = PauseVerdict.Failed;
            window.Close();
        };

        DispatcherTimer? timer = null;
        if (request.Timeout is { } timeout)
        {
            timer = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher) { Interval = timeout };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                verdict = PauseVerdict.TimedOut;
                window.Close();
            };
            timer.Start();
        }

        window.Loaded += (_, _) => note.Focus();
        window.ShowDialog();
        timer?.Stop();
        window.Dispatcher.InvokeShutdown();

        return new PauseAnswer(verdict, note.Text, stopwatch.ElapsedMilliseconds);
    }
}