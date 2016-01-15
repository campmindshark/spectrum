using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Spectrum
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Streamer st;
        public MainWindow()
        {
            InitializeComponent();
            st = new Streamer(devices);
            // remove this and uncomment below when entering production
            st.Enable = true;
            // st.Enable = false;
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            st.Enable = !st.Enable;
        }
    }
}
