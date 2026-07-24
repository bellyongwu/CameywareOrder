using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LeeYongeOrdering.Views;

public partial class DocumentPreviewWindow : Window
{
    public DocumentPreviewWindow(string imagePath, string displayName)
    {
        InitializeComponent();

        Title = displayName;
        FileNameText.Text = displayName;

        if (File.Exists(imagePath))
        {
            // Load fully into memory (OnLoad) so the file is not left locked on disk.
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(imagePath);
            bitmap.EndInit();
            bitmap.Freeze();
            PreviewImage.Source = bitmap;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
