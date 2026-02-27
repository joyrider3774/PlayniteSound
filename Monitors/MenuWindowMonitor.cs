using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Controls;
using PlayniteSounds.Controls;
using PlayniteSounds.Models;

namespace PlayniteSounds.Monitors
{

    public class MenuWindowMonitor
    {
        private static IPlayniteAPI playniteApi;
        private static PlayniteSoundsSettings settings;
        private static readonly ILogger logger = LogManager.GetLogger();

        static public void Attach(IPlayniteAPI api, PlayniteSoundsSettings settings)
        {
            playniteApi = api;
            MenuWindowMonitor.settings = settings;

            EventManager.RegisterClassHandler(typeof(Window), Window.LoadedEvent, new RoutedEventHandler(Window_Loaded));
        }

        static private void AttachLoadedEventHandlers(DependencyObject parent)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(parent))
            {
                if (child is FrameworkElement frameworkElement)
                {
                    frameworkElement.Loaded += Window_Loaded;
                    AttachLoadedEventHandlers(frameworkElement); // Recursively attach to children
                }
            }
        }

        public static OutType FindVisualChild<OutType>(DependencyObject parent, string typeName = null, string name = null) where OutType : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child != null
                    && child is OutType
                    && (typeName == null || child.GetType().Name == typeName)
                    && (name == null || (child as FrameworkElement)?.Name == name)
                )
                {
                    return (OutType)child;
                }
                else
                {
                    OutType childOfChild = FindVisualChild<OutType>(child, typeName, name);
                    if (childOfChild != null)
                    {
                        return childOfChild;
                    }
                }
            }
            return null;
        }

        private static void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window && FindVisualChild<ContentControl>(window, name: "Sounds_MusicControl") is ContentControl musicControl)
            {
                    var contextSource = window.DataContext;
                    var contextPath = "SelectedGameDetails.Game.Game";

                    var plugControl = new MusicControl(settings);

                    var binding = new Binding
                    {
                        Mode = BindingMode.OneWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.Default,
                        Path = new PropertyPath(contextPath)
                    };
                    if (contextSource != null)
                    {
                        binding.Source = contextSource;
                    }

                    BindingOperations.SetBinding(plugControl, plugControl is PluginUserControl ? PluginUserControl.GameContextProperty : Control.DataContextProperty, binding);

                    musicControl.Focusable = false;
                    musicControl.Content = plugControl;
            }
        }
    }
}
