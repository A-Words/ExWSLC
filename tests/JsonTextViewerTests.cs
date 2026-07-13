using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Threading;
using ExWSLC.Controls;
using ICSharpCode.AvalonEdit.Folding;

namespace ExWSLC.Tests;

public class JsonTextViewerTests
{
    [Fact]
    public void Viewer_IsReadOnlyAndShowsLineNumbers()
    {
        StaTest.Run(() =>
        {
            var viewer = new JsonTextViewer();

            Assert.True(viewer.IsReadOnly);
            Assert.True(viewer.ShowLineNumbers);
            Assert.False(viewer.WordWrap);
            Assert.Equal(ScrollBarVisibility.Auto, viewer.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Auto, viewer.VerticalScrollBarVisibility);
            Assert.Contains(viewer.TextArea.LeftMargins, margin => margin is FluentFoldingMargin);
            Assert.DoesNotContain(viewer.TextArea.LeftMargins, margin => margin is FoldingMargin);
        });
    }

    [Fact]
    public void JsonText_RebuildsFoldingsExpandedAndResetsCaret()
    {
        StaTest.Run(() =>
        {
            const string firstJson = "{\n  \"nested\": {\n    \"value\": true\n  }\n}";
            const string secondJson = "[\n  1,\n  2\n]";
            var viewer = new JsonTextViewer { JsonText = firstJson };
            var firstFoldings = viewer.Foldings.ToArray();
            Assert.Equal(2, firstFoldings.Length);

            firstFoldings[0].IsFolded = true;
            viewer.CaretOffset = viewer.Text.Length;
            viewer.JsonText = secondJson;

            var currentFolding = Assert.Single(viewer.Foldings);
            Assert.Equal(secondJson, viewer.Text);
            Assert.False(currentFolding.IsFolded);
            Assert.Equal(0, viewer.CaretOffset);
            Assert.Equal(0, viewer.HorizontalOffset);
            Assert.Equal(0, viewer.VerticalOffset);
        });
    }

    private static class StaTest
    {
        public static void Run(Action action)
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception caughtException)
                {
                    exception = caughtException;
                }
                finally
                {
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (exception is not null)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }
        }
    }
}
