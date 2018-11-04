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

namespace DuplicateProcedures
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class EditProcedure : UserControl
    {
        public EditProcedure()
        {
            InitializeComponent();
        }

        private void t1_LostFocus(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void t1_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            t2.ScrollToHorizontalOffset(t1.HorizontalOffset);
            t2.ScrollToVerticalOffset(t1.VerticalOffset);
        }
    }
}
