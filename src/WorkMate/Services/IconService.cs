using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingIcon = System.Drawing.Icon;

namespace WorkMate.Services;

using WorkMate.Models;

public interface IIconService : IDisposable
{
    DrawingIcon GetAppIcon();

    DrawingIcon GetTrayIcon(IconVisualState state);

    ImageSource GetStatusIcon(IconVisualState state, int pixelSize = 96);

    ImageSource GetReminderIcon(ReminderIconType reminderType, int pixelSize = 64);

    ImageSource GetActivityModeIcon(ActivityMode activityMode, int pixelSize = 64);
}

public sealed class IconService : IIconService
{
    private static readonly int[] TraySizes = [16, 20, 24, 32];
    private static readonly IReadOnlyDictionary<IconAssetKey, string> ResourcePaths =
        new Dictionary<IconAssetKey, string>
        {
            [IconAssetKey.Default] = "Assets/Icons/WorkMate_UI_IconPack/App/workmate_default.png",
            [IconAssetKey.BeforeWork] = "Assets/Icons/WorkMate_UI_IconPack/Status/before_work.png",
            [IconAssetKey.Working] = "Assets/Icons/WorkMate_UI_IconPack/Status/working.png",
            [IconAssetKey.Lunch] = "Assets/Icons/WorkMate_UI_IconPack/Status/lunch.png",
            [IconAssetKey.OffWork] = "Assets/Icons/WorkMate_UI_IconPack/Status/off_work.png",
            [IconAssetKey.Overtime] = "Assets/Icons/WorkMate_UI_IconPack/Status/overtime.png",
            [IconAssetKey.DrinkReminder] = "Assets/Icons/Generated/Actions/action_drink.png",
            [IconAssetKey.StandReminder] = "Assets/Icons/Generated/Actions/action_stand.png",
            [IconAssetKey.Cleaning] = "Assets/Icons/Generated/Actions/action_cleaning.png",
            [IconAssetKey.LabMode] = "Assets/Icons/WorkMate_UI_IconPack/Status/lab_mode.png",
            [IconAssetKey.MeetingMode] = "Assets/Icons/WorkMate_UI_IconPack/Status/meeting_mode.png"
        };

    private readonly ConcurrentDictionary<(IconAssetKey Key, int Size), DrawingIcon> _trayIcons = new();
    private readonly ConcurrentDictionary<(IconAssetKey Key, int Size), ImageSource> _images = new();
    private DrawingIcon? _appIcon;
    private bool _disposed;

    public DrawingIcon GetAppIcon()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_appIcon is not null)
        {
            return _appIcon;
        }

        using var stream = OpenResource("Assets/Icons/WorkMate.ico");
        using var icon = new DrawingIcon(stream);
        _appIcon = (DrawingIcon)icon.Clone();
        return _appIcon;
    }

    public DrawingIcon GetTrayIcon(IconVisualState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = IconStateResolver.Resolve(state);
        var size = ChooseTraySize(GetSystemDpi());
        return _trayIcons.GetOrAdd((key, size), static pair => CreateTrayIcon(pair.Key, pair.Size));
    }

    public ImageSource GetStatusIcon(IconVisualState state, int pixelSize = 96)
    {
        var key = IconStateResolver.Resolve(state);
        return GetImage(key, pixelSize);
    }

    public ImageSource GetReminderIcon(ReminderIconType reminderType, int pixelSize = 64)
    {
        var key = IconStateResolver.Resolve(
            new IconVisualState(WorkScheduleState.Working, ActivityMode.Computer, reminderType));
        return GetImage(key, pixelSize);
    }

    public ImageSource GetActivityModeIcon(ActivityMode activityMode, int pixelSize = 64)
    {
        var key = IconStateResolver.Resolve(new IconVisualState(WorkScheduleState.Working, activityMode));
        return GetImage(key, pixelSize);
    }

    public static int ChooseTraySize(double dpi)
    {
        var desired = 16 * Math.Max(1, dpi / 96d);
        return TraySizes.MinBy(size => Math.Abs(size - desired));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var icon in _trayIcons.Values)
        {
            icon.Dispose();
        }

        _trayIcons.Clear();
        _images.Clear();
        _appIcon?.Dispose();
        _appIcon = null;
    }

    private ImageSource GetImage(IconAssetKey key, int pixelSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pixelSize is < 16 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelSize));
        }

        return _images.GetOrAdd(
            (key, pixelSize),
            static pair => CreateImage(pair.Key, pair.Size));
    }

    private static DrawingIcon CreateTrayIcon(IconAssetKey key, int size)
    {
        using var stream = OpenResource(GetResourcePath(key));
        using var source = new DrawingBitmap(stream);
        using var bitmap = new DrawingBitmap(
            size,
            size,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(
                source,
                new System.Drawing.Rectangle(0, 0, size, size),
                0,
                0,
                source.Width,
                source.Height,
                System.Drawing.GraphicsUnit.Pixel);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = DrawingIcon.FromHandle(handle);
            return (DrawingIcon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static ImageSource CreateImage(IconAssetKey key, int pixelSize)
    {
        using var stream = OpenResource(GetResourcePath(key));
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = pixelSize;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static Stream OpenResource(string relativePath)
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri($"pack://application:,,,/WorkMate;component/{relativePath}", UriKind.Absolute));
        return resource?.Stream ??
               throw new FileNotFoundException($"找不到内嵌图标资源：{relativePath}");
    }

    private static string GetResourcePath(IconAssetKey key)
    {
        return ResourcePaths.TryGetValue(key, out var resourcePath)
            ? resourcePath
            : ResourcePaths[IconAssetKey.Default];
    }

    private static double GetSystemDpi()
    {
        using var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
        return graphics.DpiX;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
