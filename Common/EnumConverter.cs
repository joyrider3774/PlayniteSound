using PlayniteSounds.Models;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

//TODO: Properly investigate way to make generic EnumConverter<t> work with wpf/xaml
// Possible solution may involve factory: https://stackoverflow.com/questions/8235421/how-do-i-set-wpf-xaml-forms-design-datacontext-to-class-that-uses-generic-type/8235459#8235459
namespace PlayniteSounds.Common
{
    public class MusicTypeConverter : BaseValueConverter<MusicTypeConverter>
    {
        public override object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (int)value;

        public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => (MusicType)value;
    }

    public class AudioStateConverter : BaseValueConverter<AudioStateConverter>
    {
        public override object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (int)value;

        public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => (AudioState)value;
    }

    public abstract class BaseValueConverter<T> : MarkupExtension, IValueConverter where T : class, new()
    {
        private static T _converter;

        public override object ProvideValue(IServiceProvider serviceProvider) => _converter ?? (_converter = new T());

        public abstract object Convert(object value, Type targetType, object parameter, CultureInfo culture);

        public abstract object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture);
    }
}