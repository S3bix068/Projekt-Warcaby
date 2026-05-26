using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Warcaby;

// ── UI & input (partial) ──────────────────────────────────────────────────────

partial class GameForm : Form
{
    public GameForm()
    {
        SetupForm();
        StartNewGame();
    }

    void SetupForm()
    {
        Text        = "Warcaby";
        int boardPx = 8 * CellSize + 2 * BoardMargin;
        ClientSize  = new Size(boardPx + SidebarWidth, boardPx);
        DoubleBuffered  = true;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        Paint      += OnPaint;
        MouseClick += OnMouseClick;

        var newGameButton = new Button
        {
            Text     = "Nowa gra",
            Location = new Point(boardPx + 10, boardPx - 44),
            Size     = new Size(SidebarWidth - 20, 34),
            Font     = new Font("Segoe UI", 10)
        };
        newGameButton.Click += (_, _) => StartNewGame();
        Controls.Add(newGameButton);
    }

    // ── Input handling ────────────────────────────────────────────────────────

    void OnMouseClick(object? sender, MouseEventArgs e)
    {
        int col = (e.X - BoardMargin) / CellSize;
        int row = (e.Y - BoardMargin) / CellSize;
        if (!InBounds(row, col)) return;

        var clicked = new Point(col, row);

        // During multi-capture only the highlighted targets are allowed
        if (multiCaptureMode)
        {
            TryExecuteMove(clicked);
            return;
        }

        // Execute a move if a piece is already selected and the click lands on a valid target
        if (selectedCell.HasValue && TryExecuteMove(clicked))
            return;

        // Otherwise try to select a piece
        TrySelectPiece(clicked);
    }

    void TrySelectPiece(Point pos)
    {
        var piece = board[pos.Y, pos.X];
        if (piece == null || piece.Owner != currentPlayer) return;

        // Pieces that have no entry in legalMoves cannot be selected
        // (e.g. when mandatory capture exists but this piece cannot capture)
        var moves = legalMoves.Where(m => m.From == pos).ToList();
        if (moves.Count == 0) return;

        selectedCell = pos;
        pieceMoves   = moves;
        Invalidate();
    }

    bool TryExecuteMove(Point destination)
    {
        var move = pieceMoves.FirstOrDefault(m => m.To == destination);
        if (move == null) return false;
        ApplyMove(move);
        return true;
    }

    // ── Drawing ───────────────────────────────────────────────────────────────

    void OnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        DrawBoard(g);
        DrawPieces(g);
        DrawSidebar(g);
    }

    void DrawBoard(Graphics g)
    {
        for (int row = 0; row < 8; row++)
            for (int col = 0; col < 8; col++)
            {
                bool dark = (row + col) % 2 == 1;
                var rect  = CellRect(row, col);
                g.FillRectangle(dark ? Brushes.SaddleBrown : Brushes.Wheat, rect);

                // Highlight selected cell
                if (selectedCell == new Point(col, row))
                {
                    using var b = new SolidBrush(Color.FromArgb(170, 255, 220, 0));
                    g.FillRectangle(b, rect);
                }
                // Highlight valid landing squares
                else if (pieceMoves.Any(m => m.To == new Point(col, row)))
                {
                    using var b = new SolidBrush(Color.FromArgb(130, 0, 210, 0));
                    g.FillRectangle(b, rect);
                }
            }

        using var pen = new Pen(Color.Black, 2);
        g.DrawRectangle(pen, BoardMargin, BoardMargin, 8 * CellSize, 8 * CellSize);
    }

    void DrawPieces(Graphics g)
    {
        for (int row = 0; row < 8; row++)
            for (int col = 0; col < 8; col++)
            {
                var piece = board[row, col];
                if (piece == null) continue;

                var cell = CellRect(row, col);
                const int pad = 7;
                var rect = new Rectangle(cell.X + pad, cell.Y + pad,
                                         CellSize - 2 * pad, CellSize - 2 * pad);

                bool isWhite = piece.Owner == Player.White;
                var fill     = isWhite ? Color.FromArgb(248, 248, 240) : Color.FromArgb(30, 30, 30);
                var border   = isWhite ? Color.FromArgb(110, 110, 110) : Color.FromArgb(200, 200, 200);

                // Drop shadow
                using (var shadow = new SolidBrush(Color.FromArgb(55, 0, 0, 0)))
                    g.FillEllipse(shadow, new Rectangle(rect.X + 3, rect.Y + 3, rect.Width, rect.Height));

                using (var fb = new SolidBrush(fill))
                using (var bp = new Pen(border, 2))
                {
                    g.FillEllipse(fb, rect);
                    g.DrawEllipse(bp, rect);
                }

                // King crown symbol
                if (piece.Type == PieceType.King)
                {
                    using var font = new Font("Segoe UI Symbol", 18, FontStyle.Bold);
                    var sf = new StringFormat
                    {
                        Alignment     = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString("♛", font, Brushes.Gold, rect, sf);
                }
            }
    }

    void DrawSidebar(Graphics g)
    {
        int x = 8 * CellSize + 2 * BoardMargin + 10;
        int y = BoardMargin;

        using var titleFont  = new Font("Segoe UI", 11, FontStyle.Bold);
        using var normalFont = new Font("Segoe UI", 10);

        // ── Current turn indicator ─────────────────────────────
        g.DrawString("Tura:", titleFont, Brushes.Black, x, y);
        y += 26;

        bool whiteTurn = currentPlayer == Player.White;
        var  fill      = whiteTurn ? Color.FromArgb(248, 248, 240) : Color.FromArgb(30, 30, 30);
        var  border    = whiteTurn ? Color.Gray : Color.Silver;

        var circle = new Rectangle(x, y, 28, 28);
        using (var fb = new SolidBrush(fill)) g.FillEllipse(fb, circle);
        using (var bp = new Pen(border, 2))   g.DrawEllipse(bp, circle);
        g.DrawString(whiteTurn ? "Białe" : "Czarne", normalFont, Brushes.Black, x + 36, y + 5);
        y += 44;

        // ── Multi-capture notice ───────────────────────────────
        if (multiCaptureMode)
        {
            using var alertFont = new Font("Segoe UI", 9, FontStyle.Bold);
            g.DrawString("Kontynuuj bicie!", alertFont, Brushes.DarkRed, x, y);
            y += 22;
        }

        // ── Piece counters ─────────────────────────────────────
        y += 10;
        g.DrawString("Pionki:", titleFont, Brushes.Black, x, y);
        y += 26;

        int wCount = 0, bCount = 0;
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                if (board[r, c] == null) continue;
                if (board[r, c]!.Owner == Player.White) wCount++;
                else bCount++;
            }

        DrawPieceCounter(g, normalFont, x, y,
            wCount, Color.FromArgb(248, 248, 240), Color.Gray, "Białe");
        y += 24;
        DrawPieceCounter(g, normalFont, x, y,
            bCount, Color.FromArgb(30, 30, 30), Color.Silver, "Czarne");
    }

    static void DrawPieceCounter(Graphics g, Font font, int x, int y,
                                  int count, Color fill, Color border, string label)
    {
        var dot = new Rectangle(x, y + 3, 14, 14);
        using (var fb = new SolidBrush(fill)) g.FillEllipse(fb, dot);
        using (var bp = new Pen(border, 1.5f)) g.DrawEllipse(bp, dot);
        g.DrawString($"{label}: {count}", font, Brushes.Black, x + 20, y);
    }

    Rectangle CellRect(int row, int col) =>
        new(BoardMargin + col * CellSize, BoardMargin + row * CellSize, CellSize, CellSize);

    // ── Entry point ───────────────────────────────────────────────────────────

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new GameForm());
    }
}
