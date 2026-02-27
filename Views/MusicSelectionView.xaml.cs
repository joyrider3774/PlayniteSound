using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using PlayniteSounds.ViewModels;
using System;
using System.Globalization;
using System.Windows.Data;
using PlayniteSounds.Downloaders;
using PlayniteSounds.Models;
using System.IO;
using System.Windows.Markup;


namespace PlayniteSounds.Views
{

    public class IsPreviewingItemConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var preview = values[1] as string;
            return !string.IsNullOrEmpty(preview)
                && (values[0] as GenericObjectOption)?.Object is Song song
                && DownloadManager.GetTempPath(song) == preview;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IsCachedItemConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            return (values[0] as GenericObjectOption)?.Object is Song song && File.Exists(DownloadManager.GetTempPath(song));
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class MusicSelectionView : UserControl
    {
        public MusicSelectionView()
        {
            ((IComponentConnector)this).InitializeComponent();
            Loaded += MusicSelectionView_Loaded;
            Unloaded += MusicSelectionView_Unloaded;
            ListSearch.SelectionChanged += ListSearch_OnSelectionChanged;
        }

        private void MusicSelectionView_Loaded(object sender, RoutedEventArgs e)
        {
            MusicSelectionViewModel model = DataContext as MusicSelectionViewModel;
            if (!(model.SearchResults?.Count > 0 ))
            {
                model.Search();
            }
        }

        private void MusicSelectionView_Unloaded(object sender, RoutedEventArgs e)
        {
            (DataContext as MusicSelectionViewModel).Close();
            Loaded -= MusicSelectionView_Loaded;
            Unloaded -= MusicSelectionView_Unloaded;
            ListSearch.SelectionChanged -= ListSearch_OnSelectionChanged;
        }

        private void ListSearch_OnSelectionChanged(object sender, RoutedEventArgs e) {
            (DataContext as MusicSelectionViewModel).SelectedResult = ListSearch.SelectedItems.Cast<GenericItemOption>().ToList();
        }
    }
}
