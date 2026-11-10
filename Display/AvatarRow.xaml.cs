using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Owmeta.Display
{
    /// <summary>
    /// A labeled row of hero avatars. Items need an <c>Icon</c> and an <c>IsHard</c> property.
    /// The row collapses itself when it has nothing to show.
    /// </summary>
    public partial class AvatarRow : UserControl
    {
        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
            nameof(Label), typeof(string), typeof(AvatarRow),
            new PropertyMetadata(string.Empty, (d, e) => ((AvatarRow)d).LabelText.Text = (string)e.NewValue));

        public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
            nameof(Items), typeof(IEnumerable), typeof(AvatarRow),
            new PropertyMetadata(null, (d, _) => ((AvatarRow)d).OnItemsChanged()));

        public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
            nameof(Accent), typeof(Brush), typeof(AvatarRow), new PropertyMetadata(null));

        public static readonly DependencyProperty AvatarSizeProperty = DependencyProperty.Register(
            nameof(AvatarSize), typeof(double), typeof(AvatarRow), new PropertyMetadata(40.0));

        public static readonly DependencyProperty LabelWidthProperty = DependencyProperty.Register(
            nameof(LabelWidth), typeof(double), typeof(AvatarRow),
            new PropertyMetadata(92.0, (d, e) => ((AvatarRow)d).LabelColumn.Width = new GridLength((double)e.NewValue)));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public IEnumerable? Items
        {
            get => (IEnumerable?)GetValue(ItemsProperty);
            set => SetValue(ItemsProperty, value);
        }

        public Brush? Accent
        {
            get => (Brush?)GetValue(AccentProperty);
            set => SetValue(AccentProperty, value);
        }

        public double AvatarSize
        {
            get => (double)GetValue(AvatarSizeProperty);
            set => SetValue(AvatarSizeProperty, value);
        }

        public double LabelWidth
        {
            get => (double)GetValue(LabelWidthProperty);
            set => SetValue(LabelWidthProperty, value);
        }

        public AvatarRow()
        {
            InitializeComponent();
            Visibility = Visibility.Collapsed;
        }

        private void OnItemsChanged()
        {
            ItemsHost.ItemsSource = Items;
            bool hasItems = Items?.GetEnumerator().MoveNext() ?? false;
            Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
