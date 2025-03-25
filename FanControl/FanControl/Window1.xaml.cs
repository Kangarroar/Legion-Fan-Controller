
using System.Windows;


namespace FanControlApp
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();


            SliderFan1.ValueChanged += Slider_ValueChanged;
            SliderFan2.ValueChanged += Slider_ValueChanged;

        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

        }

        private void TurboButton_Click(object sender, RoutedEventArgs e)
        {

        }

        private void SliderFan1_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

        }

        private void SliderFan4_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

        }
    }

}
