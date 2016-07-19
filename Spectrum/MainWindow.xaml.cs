using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Spectrum
{
    public partial class MainWindow : Window
    {
        Streamer st;
        private bool dragStarted = true;
        private bool boxInitialized = false;
        public MainWindow()
        {
            InitializeComponent();
            st = new Streamer(devices);
            st.Enable = false;
            boxInitialized = true;
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            st.controlLights = (bool)checkBox.IsChecked;
            dropQuietS.IsEnabled = !dropQuietS.IsEnabled;
            dropChangeS.IsEnabled = !dropChangeS.IsEnabled;
            kickQuietS.IsEnabled = !kickQuietS.IsEnabled;
            kickChangeS.IsEnabled = !kickChangeS.IsEnabled;
            snareQuietS.IsEnabled = !snareQuietS.IsEnabled;
            snareChangeS.IsEnabled = !snareChangeS.IsEnabled;
            peakChangeS.IsEnabled = !peakChangeS.IsEnabled;
            st.Enable = !st.Enable;
            dragStarted = false;
        }

        private void Slider_DragStarted(object sender, DragStartedEventArgs e)
        {
            dragStarted = true;
        }
        private void Slider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            set(((Slider)sender).Name, ((Slider)sender).Value);
            dragStarted = false;
        }
        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!dragStarted)
                set(((Slider)sender).Name, e.NewValue);
        }
        private void HandleCheck(object sender, RoutedEventArgs e)
        {
            if(boxInitialized)
                set("controlLights", 1);
        }

        private void HandleUnchecked(object sender, RoutedEventArgs e)
        {
            set("controlLights", 0);
        }
        private void set(String name, double val)
        {
            st.updateConstants(name, (float)val);
            if (name == "dropQuietS")
            {
                dropQuietL.Content = val.ToString("F3");
            }
            if (name == "dropChangeS")
            {
                dropChangeL.Content = val.ToString("F3");
            }
            if (name == "kickQuietS")
            {
                kickQuietL.Content = val.ToString("F3");
            }
            if (name == "kickChangeS")
            {
                kickChangeL.Content = val.ToString("F3");
            }
            if (name == "snareQuietS")
            {
                snareQuietL.Content = val.ToString("F3");
            }
            if (name == "snareChangeS")
            {
                snareChangeL.Content = val.ToString("F3");
            }
            if (name == "peakChangeS")
            {
                peakChangeL.Content = val.ToString("F3");
            }
        }
    }
}
