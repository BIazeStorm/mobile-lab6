namespace Lab6;

public partial class MainPage
{
    private enum CellStatus
    {
        Idle,
        Absent,
        Present,
        Correct
    }

    private readonly string[][] _keyboardRows =
    [
        ["Й", "Ц", "У", "К", "Е", "Н", "Г", "Ш", "Щ", "З", "Х", "Ї"],
        ["Ф", "І", "В", "А", "П", "Р", "О", "Л", "Д", "Ж", "Є"],
        ["ENTER", "Я", "Ч", "С", "М", "И", "Т", "Ь", "Б", "Ю", "DEL"]
    ];

    private readonly Border[,] _cellBorders = new Border[6, 5];
    private readonly Label[,] _cellLabels = new Label[6, 5];
    private readonly Dictionary<string, Border> _keyBorders = new();

    private string _targetWord = string.Empty;
    private readonly string[] _guesses = new string[6];
    private int _currentTry;
    private string _currentGuess = string.Empty;
    private bool _isGameOver;
    private bool _isAnimating;

    private CancellationTokenSource? _toastCts;

    public MainPage()
    {
        InitializeComponent();
        BuildBoard();
        BuildKeyboard();
        StartNewGame();
    }

    private void BuildBoard()
    {
        BoardContainer.Children.Clear();

        for (int r = 0; r < 6; r++)
        {
            var rowStack = new HorizontalStackLayout
            {
                Spacing = 8,
                HorizontalOptions = LayoutOptions.Center
            };

            for (int c = 0; c < 5; c++)
            {
                var label = new Label
                {
                    Text = string.Empty,
                    TextColor = Colors.White,
                    FontSize = 24,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                };

                var border = new Border
                {
                    WidthRequest = 52,
                    HeightRequest = 52,
                    BackgroundColor = Colors.Transparent,
                    Stroke = Color.FromArgb("#334155"),
                    StrokeThickness = 2,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                    Content = label
                };

                _cellBorders[r, c] = border;
                _cellLabels[r, c] = label;
                rowStack.Children.Add(border);
            }

            BoardContainer.Children.Add(rowStack);
        }
    }

    private void BuildKeyboard()
    {
        KeyboardContainer.Children.Clear();
        _keyBorders.Clear();

        foreach (var row in _keyboardRows)
        {
            var gridRow = new Grid
            {
                ColumnSpacing = 4,
                HorizontalOptions = LayoutOptions.Fill
            };

            for (int i = 0; i < row.Length; i++)
            {
                string key = row[i];
                bool isSpecial = key is "ENTER" or "DEL";

                gridRow.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(isSpecial ? 1.5 : 1.0, GridUnitType.Star)
                });

                var label = new Label
                {
                    Text = key,
                    TextColor = Colors.White,
                    FontSize = isSpecial ? 11 : 13,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                };

                var keyBorder = new Border
                {
                    HeightRequest = 46,
                    BackgroundColor = Color.FromArgb("#334155"),
                    StrokeThickness = 0,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
                    Content = label
                };

                var tapGesture = new TapGestureRecognizer();
                tapGesture.Tapped += async (_, _) => await HandleKeyPressAsync(key);
                keyBorder.GestureRecognizers.Add(tapGesture);

                _keyBorders[key] = keyBorder;
                gridRow.Add(keyBorder, i);
            }

            KeyboardContainer.Children.Add(gridRow);
        }
    }

    private void StartNewGame()
    {
        _toastCts?.Cancel();
        ToastBanner.IsVisible = false;
        EndGameOverlay.IsVisible = false;

        _targetWord = Dictionary.Words[Random.Shared.Next(Dictionary.Words.Length)].ToUpperInvariant();
        Array.Fill(_guesses, string.Empty);
        _currentTry = 0;
        _currentGuess = string.Empty;
        _isGameOver = false;
        _isAnimating = false;

        for (int r = 0; r < 6; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                _cellLabels[r, c].Text = string.Empty;
                _cellBorders[r, c].ScaleY = 1.0;
                _cellBorders[r, c].BackgroundColor = Colors.Transparent;
                _cellBorders[r, c].Stroke = Color.FromArgb("#334155");
            }
        }

        UpdateKeyboardColors();
    }

    private async Task TriggerErrorAsync(string message)
    {
        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
        }
        catch
        {
            // Ігноруємо на пристроях без вібромотора
        }

        // Тремтіння ігрового поля
        for (int i = 0; i < 3; i++)
        {
            await BoardContainer.TranslateToAsync(-10, 0, 45);
            await BoardContainer.TranslateToAsync(10, 0, 45);
        }
        await BoardContainer.TranslateToAsync(0, 0, 40);

        // Показ Toast-повідомлення
        _toastCts?.Cancel();
        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;

        ToastLabel.Text = message;
        ToastBanner.IsVisible = true;

        try
        {
            await Task.Delay(2000, token);
            ToastBanner.IsVisible = false;
        }
        catch (TaskCanceledException)
        {
            // Попередній таймер скасовано новим повідомленням
        }
    }

    private static CellStatus[] EvaluateRow(string guess, string target)
    {
        var result = new CellStatus[5];
        Array.Fill(result, CellStatus.Absent);

        if (guess.Length != 5 || target.Length != 5)
            return result;

        char[] targetChars = target.ToUpperInvariant().ToCharArray();
        char[] guessChars = guess.ToUpperInvariant().ToCharArray();

        // 1. Точні збіги на своїх місцях
        for (int i = 0; i < 5; i++)
        {
            if (guessChars[i] == targetChars[i])
            {
                result[i] = CellStatus.Correct;
                targetChars[i] = '\0';
                guessChars[i] = '\0';
            }
        }

        // 2. Літери, які є в слові на інших позиціях
        for (int i = 0; i < 5; i++)
        {
            char ch = guessChars[i];
            if (ch != '\0')
            {
                int index = Array.IndexOf(targetChars, ch);
                if (index != -1)
                {
                    result[i] = CellStatus.Present;
                    targetChars[index] = '\0';
                }
            }
        }

        return result;
    }

    private async Task HandleKeyPressAsync(string key)
    {
        if (_isGameOver || _isAnimating)
            return;

        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        catch (FeatureNotSupportedException)
        {
        }

        if (key == "ENTER")
        {
            if (_currentGuess.Length != 5)
            {
                await TriggerErrorAsync("Слово має містити 5 літер");
                return;
            }
            if (!Dictionary.WordSet.Contains(_currentGuess))
            {
                await TriggerErrorAsync("Такого слова немає у словнику!");
                return;
            }
            if (_guesses.Take(_currentTry).Contains(_currentGuess))
            {
                await TriggerErrorAsync("Ви вже вводили це слово!");
                return;
            }

            string submittedWord = _currentGuess;
            int rowIndex = _currentTry;
            CellStatus[] statuses = EvaluateRow(submittedWord, _targetWord);

            _isAnimating = true;
            _guesses[rowIndex] = submittedWord;

            await AnimateRowFlipAsync(rowIndex, statuses);
            _isAnimating = false;

            if (submittedWord == _targetWord)
            {
                _isGameOver = true;
                UpdateKeyboardColors();
                ShowEndGameModal(isWin: true);
            }
            else if (rowIndex == 5)
            {
                _isGameOver = true;
                UpdateKeyboardColors();
                ShowEndGameModal(isWin: false);
            }
            else
            {
                _currentTry = rowIndex + 1;
                _currentGuess = string.Empty;
                UpdateKeyboardColors();
            }
        }
        else if (key == "DEL")
        {
            if (_currentGuess.Length > 0)
            {
                _currentGuess = _currentGuess[..^1];
                RenderCurrentRow();
            }
        }
        else
        {
            if (_currentGuess.Length < 5)
            {
                _currentGuess += key;
                RenderCurrentRow();
            }
        }
    }

    private void RenderCurrentRow()
    {
        for (int c = 0; c < 5; c++)
        {
            _cellLabels[_currentTry, c].Text = c < _currentGuess.Length
                ? _currentGuess[c].ToString()
                : string.Empty;
        }
    }

    private async Task AnimateRowFlipAsync(int rowIndex, CellStatus[] statuses)
    {
        for (int col = 0; col < 5; col++)
        {
            var border = _cellBorders[rowIndex, col];
            var status = statuses[col];

            await border.ScaleYToAsync(0, 130, Easing.CubicIn);
            ApplyStatusToCell(border, status);
            await border.ScaleYToAsync(1, 130, Easing.CubicOut);
        }
    }

    private static void ApplyStatusToCell(Border border, CellStatus status)
    {
        Color color = status switch
        {
            CellStatus.Correct => Color.FromArgb("#10B981"),
            CellStatus.Present => Color.FromArgb("#F59E0B"),
            CellStatus.Absent => Color.FromArgb("#475569"),
            _ => Colors.Transparent
        };

        border.BackgroundColor = color;
        border.Stroke = status == CellStatus.Idle ? Color.FromArgb("#334155") : color;
    }

    private void UpdateKeyboardColors()
    {
        int limit = _isGameOver ? _currentTry + 1 : _currentTry;

        foreach (var (key, border) in _keyBorders)
        {
            if (key is "ENTER" or "DEL")
            {
                border.BackgroundColor = Color.FromArgb("#475569");
                continue;
            }

            int priority = 0;
            for (int i = 0; i < limit; i++)
            {
                string word = _guesses[i];
                if (word.Length != 5) continue;

                CellStatus[] statuses = EvaluateRow(word, _targetWord);
                for (int j = 0; j < 5; j++)
                {
                    if (word[j].ToString() == key)
                    {
                        int p = statuses[j] switch
                        {
                            CellStatus.Correct => 3,
                            CellStatus.Present => 2,
                            CellStatus.Absent => 1,
                            _ => 0
                        };
                        if (p > priority) priority = p;
                    }
                }
            }

            string hex = priority switch
            {
                3 => "#10B981",
                2 => "#F59E0B",
                1 => "#1E293B",
                _ => "#334155"
            };

            border.BackgroundColor = Color.FromArgb(hex);
        }
    }

    private void ShowEndGameModal(bool isWin)
    {
        EndTitleLabel.Text = isWin ? "Перемога! 🎉" : "Гру завершено";
        EndSubtitleLabel.Text = isWin
            ? "Ви відгадали слово!"
            : "Спроби закінчилися. Загадане слово:";

        TargetWordLabel.Text = _targetWord;
        TargetWordBadge.Stroke = Color.FromArgb(isWin ? "#10B981" : "#F59E0B");
        EndGameOverlay.IsVisible = true;
    }

    private void OnRestartClicked(object? sender, EventArgs e) => StartNewGame();

    private void OnHelpClicked(object? sender, EventArgs e) => RulesOverlay.IsVisible = true;

    private void OnCloseRulesClicked(object? sender, EventArgs e) => RulesOverlay.IsVisible = false;

    private void OnCloseEndGameClicked(object? sender, EventArgs e) => EndGameOverlay.IsVisible = false;

    private void OnPlayAgainClicked(object? sender, EventArgs e) => StartNewGame();
}