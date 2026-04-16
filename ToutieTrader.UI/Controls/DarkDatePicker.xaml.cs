using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ToutieTrader.UI.Controls;

public partial class DarkDatePicker : UserControl
{
    // ── DP SelectedDate ───────────────────────────────────────────────────────
    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime?), typeof(DarkDatePicker),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, _) => ((DarkDatePicker)d).RefreshDisplay()));

    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public event EventHandler<DateTime>? DateSelected;

    // ── État ──────────────────────────────────────────────────────────────────
    private DateTime _view; // premier jour du mois affiché

    // ── Couleurs (hardcoded — zéro dépendance au thème) ──────────────────────
    private static readonly SolidColorBrush BrRed      = new(Color.FromRgb(0xE8, 0x00, 0x2D));
    private static readonly SolidColorBrush BrBg       = new(Color.FromRgb(0x0D, 0x0D, 0x0F));
    private static readonly SolidColorBrush BrHover    = new(Color.FromRgb(0x22, 0x22, 0x28));
    private static readonly SolidColorBrush BrMuted    = new(Color.FromRgb(0x5A, 0x5A, 0x72));
    private static readonly SolidColorBrush BrTransp   = Brushes.Transparent;
    private static readonly SolidColorBrush BrWhite    = Brushes.White;

    public DarkDatePicker()
    {
        InitializeComponent();
        _view = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        RefreshDisplay();
    }

    // ── Affichage du champ ────────────────────────────────────────────────────
    private void RefreshDisplay()
    {
        TxtDate.Text = SelectedDate.HasValue
            ? SelectedDate.Value.ToString("yyyy-MM-dd")
            : "—";
    }

    // ── Ouverture popup ───────────────────────────────────────────────────────
    private void BdMain_Click(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;
        if (SelectedDate.HasValue)
            _view = new DateTime(SelectedDate.Value.Year, SelectedDate.Value.Month, 1);
        BuildCalendar();
        Pop.IsOpen = true;
    }

    // ── Navigation mois ───────────────────────────────────────────────────────
    private void BtnPrev_Click(object sender, RoutedEventArgs e)
    {
        _view = _view.AddMonths(-1);
        BuildCalendar();
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        _view = _view.AddMonths(1);
        BuildCalendar();
    }

    // ── Construction de la grille ─────────────────────────────────────────────
    private void BuildCalendar()
    {
        TxtHeader.Text = _view.ToString("MMMM yyyy");

        GrdDays.Children.Clear();
        GrdDays.ColumnDefinitions.Clear();
        GrdDays.RowDefinitions.Clear();

        for (int c = 0; c < 7; c++)
            GrdDays.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        for (int r = 0; r < 7; r++)   // 1 ligne headers + 6 semaines
            GrdDays.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

        // En-têtes jours (D L M M J V S)
        string[] hdrs = { "D", "L", "M", "M", "J", "V", "S" };
        for (int c = 0; c < 7; c++)
            AddCell(new TextBlock
            {
                Text = hdrs[c],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                FontSize   = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrMuted,
            }, 0, c);

        // Jours du mois
        int startCol   = (int)_view.DayOfWeek;          // 0=Dim
        int daysInMonth = DateTime.DaysInMonth(_view.Year, _view.Month);

        for (int d = 1; d <= daysInMonth; d++)
        {
            int idx  = startCol + d - 1;
            int col  = idx % 7;
            int row  = idx / 7 + 1;

            var date       = new DateTime(_view.Year, _view.Month, d);
            bool isSelected = SelectedDate?.Date == date;
            bool isToday    = date == DateTime.Today;

            var bd = new Border
            {
                CornerRadius  = new CornerRadius(2),
                Background    = isSelected ? BrRed : BrTransp,
                BorderBrush   = isToday && !isSelected ? BrRed : BrTransp,
                BorderThickness = new Thickness(1),
                Cursor        = Cursors.Hand,
                Tag           = date,
            };
            bd.Child = new TextBlock
            {
                Text = d.ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                FontSize   = 11,
                Foreground = isSelected ? BrWhite : BrRed,
            };

            bd.MouseLeftButtonUp += DayClick;
            bd.MouseEnter        += DayEnter;
            bd.MouseLeave        += DayLeave;

            AddCell(bd, row, col);
        }
    }

    // ── Handlers jours ────────────────────────────────────────────────────────
    private void DayClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: DateTime date }) return;
        SelectedDate = date;
        DateSelected?.Invoke(this, date);
        Pop.IsOpen = false;
    }

    private void DayEnter(object sender, MouseEventArgs e)
    {
        if (sender is not Border { Tag: DateTime date } bd) return;
        if (SelectedDate?.Date != date)
            bd.Background = BrHover;
    }

    private void DayLeave(object sender, MouseEventArgs e)
    {
        if (sender is not Border { Tag: DateTime date } bd) return;
        bd.Background = SelectedDate?.Date == date ? BrRed : BrTransp;
    }

    // ── Helper placement grille ───────────────────────────────────────────────
    private void AddCell(UIElement el, int row, int col)
    {
        Grid.SetRow(el, row);
        Grid.SetColumn(el, col);
        GrdDays.Children.Add(el);
    }
}
