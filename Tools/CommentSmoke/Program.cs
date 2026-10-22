using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LectImageRenamer;
using LectImageRenamer.Models;
using LectImageRenamer.ViewModels;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string root = Path.GetFullPath("build-verify/comments-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        string a = Path.Combine(root, "images"), b = Path.Combine(root, "other");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        CreateImage(Path.Combine(a, "01-landscape.png"), 480, 300, Colors.CornflowerBlue);
        CreateImage(Path.Combine(a, "02-portrait.png"), 300, 480, Colors.Coral);
        CreateImage(Path.Combine(a, "03-square.png"), 400, 400, Colors.SeaGreen);
        CreateImage(Path.Combine(b, "01-landscape.png"), 480, 300, Colors.SlateBlue);
        Application app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        MainWindow window = new() { Left = 40, Top = 40 };
        var vm = (MainWindowViewModel)window.DataContext;
        vm.AddPaths([a, b]);
        Require(vm.Images.Count == 4, "load fixture");
        Require(!File.Exists(Path.Combine(a, FolderComments.FileName)), "viewing creates no JSON");
        window.Show();
        Pump();
        Save(window, Path.Combine(root, "normal.png"));
        vm.IsGridView = false;
        vm.IsCommentMode = true;
        Require(vm.ShowGrid, "comment mode uses grid");
        Pump();
        var first = vm.Images[0];
        var input = Descendants(window).OfType<CommentEditor>().First(editor => ReferenceEquals(editor.DataContext, first));
        var box = Descendants(input).OfType<TextBox>().Single();
        box.Text = "背景を少し明るくしてください。\n右端の不要物を削除。\n色味は維持。";
        Pump(650);
        Require(first.Comment == box.Text, "grid input binding");
        var data = Read(a);
        Require(data.Length == 1 && data[0].Comment == box.Text && data[0].SavedAt != default, "autosave Japanese multiline and timestamp");
        vm.Images[3].Comment = "別フォルダーのコメント";
        Require(vm.FlushComments() && Read(b).Single().Comment == vm.Images[3].Comment, "folder isolation");
        var reload = new MainWindowViewModel();
        reload.AddPaths([first.FullPath]);
        Require(reload.Images.Single().Comment == first.Comment, "reload comments");
        PreviewWindow preview = new() { Left = 1180, Top = 40, Width = 620, Height = 540, IsCommentMode = true };
        preview.ShowImage(first);
        Pump();
        var previewBox = Descendants(preview).OfType<TextBox>().Single();
        previewBox.Text = "プレビューから修正。\n背景を明るく。\n輪郭は維持。";
        Pump(650);
        Require(box.Text == previewBox.Text && first.Comment == previewBox.Text, "preview to grid sync");
        Save(window, Path.Combine(root, "comments.png"));
        Save(preview, Path.Combine(root, "preview.png"));
        window.Width = 860;
        vm.GridItemSize = 96;
        Pump();
        Save(window, Path.Combine(root, "minimum.png"));
        window.Width = 1120;
        vm.GridItemSize = 160;
        vm.IsCommentMode = false;
        Require(!vm.ShowGrid && !vm.IsGridView, "restore prior list layout");
        Pump();
        Save(window, Path.Combine(root, "list.png"));
        vm.IsCommentMode = true;
        vm.Images[1].Comment = "二枚目のコメント";
        vm.FlushComments();
        // Reload only one file: edits must not erase comments for unloaded files.
        var subset = new MainWindowViewModel();
        subset.AddPaths([first.FullPath]);
        subset.Images[0].Comment = first.Comment;
        subset.FlushComments();
        Require(Read(a).Length == 2, "preserve unloaded comments");
        first.Comment = "リネーム直前の入力";
        vm.RenamePrefix = @"checked_\nnn";
        vm.RenameCommand.Execute(null);
        Require(Read(a).Any(record => record.FileName == first.FileName && record.Comment == first.Comment), "rename and flush pending input");
        Require(preview.Title.Contains(first.FileName), "preview name follows rename");
        vm.Images[1].Comment = "";
        vm.FlushComments();
        Require(Read(a).Length == 1, "clear comment");
        first.Comment = "一覧クリア前の入力";
        preview.ClearImage();
        vm.ClearCommand.Execute(null);
        vm.AddPaths([a, b]);
        Require(vm.Images[0].Comment == "一覧クリア前の入力", "clear flush and reload");
        var bad = Path.Combine(root, "invalid");
        Directory.CreateDirectory(bad);
        File.WriteAllText(Path.Combine(bad, FolderComments.FileName), "invalid JSON");
        var invalid = new FolderComments(bad);
        Require(!invalid.CanEdit && File.ReadAllText(invalid.FilePath) == "invalid JSON", "invalid JSON preserved");
        using (var locked = new FileStream(Path.Combine(a, FolderComments.FileName), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            vm.Images[0].Comment = "保存エラーから再試行";
            Require(!vm.FlushComments() && vm.Images[0].CommentDocument!.Status.StartsWith("保存失敗"), "save error visible");
        }
        Require(vm.FlushComments(), "retry save");
        Console.WriteLine("COMMENT_SMOKE_OK: autosave, reload, folder isolation, grid/preview binding, mode restore, rename, clear, and save failure.");
        Console.WriteLine(root);
        if (args.Contains("--interactive"))
        {
            preview.Close();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.MainWindow = window;
            app.Run();
        }
        else
        {
            preview.Close();
            window.Close();
            app.Shutdown();
        }
    }

    private static FolderComments.CommentRecord[] Read(string directory) => JsonSerializer.Deserialize<FolderComments.CommentRecord[]>(
        File.ReadAllText(Path.Combine(directory, FolderComments.FileName)), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static void Require(bool value, string label)
    {
        if (!value) throw new Exception("Failed: " + label);
    }

    private static void Pump(int milliseconds = 50)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Save(Window window, string path)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void CreateImage(string path, int width, int height, Color color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, width, height));
            dc.DrawEllipse(Brushes.WhiteSmoke, null, new Point(width * .65, height * .3), 40, 40);
            dc.DrawRectangle(Brushes.MidnightBlue, null, new Rect(20, height * .65, width - 40, height * .25));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
