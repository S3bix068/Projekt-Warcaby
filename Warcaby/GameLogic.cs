using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Warcaby;

// ── Data types ────────────────────────────────────────────────────────────────

enum Player { White, Black }
enum PieceType { Man, King }

class Piece(Player owner, PieceType type = PieceType.Man)
{
    public Player    Owner = owner;
    public PieceType Type  = type;
}

class Move(Point from, Point to, Point? captured = null)
{
    public readonly Point  From     = from;
    public readonly Point  To       = to;
    public readonly Point? Captured = captured;   // null for a simple (non-capture) move
    public bool IsCapture => Captured.HasValue;
}

// ── Game logic (partial) ──────────────────────────────────────────────────────

partial class GameForm
{
    // Layout constants
    const int CellSize     = 65;
    const int BoardMargin  = 20;
    const int SidebarWidth = 160;

    // Game state
    Piece?[,]  board           = new Piece[8, 8];
    Player     currentPlayer   = Player.White;
    Point?     selectedCell    = null;
    List<Move> legalMoves      = [];   // all legal moves for currentPlayer
    List<Move> pieceMoves      = [];   // legal moves for the selected piece
    bool       multiCaptureMode = false;

    void StartNewGame()
    {
        board            = new Piece[8, 8];
        currentPlayer    = Player.White;
        selectedCell     = null;
        pieceMoves       = [];
        multiCaptureMode = false;

        // Black pieces on rows 0-2
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 8; col++)
                if ((row + col) % 2 == 1)
                    board[row, col] = new Piece(Player.Black);

        // White pieces on rows 5-7
        for (int row = 5; row < 8; row++)
            for (int col = 0; col < 8; col++)
                if ((row + col) % 2 == 1)
                    board[row, col] = new Piece(Player.White);

        ComputeLegalMoves();
        Invalidate();
    }

    // ── Move generation ───────────────────────────────────────────────────────

    void ComputeLegalMoves()
    {
        var captures = new List<Move>();
        var simples  = new List<Move>();

        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                var piece = board[r, c];
                if (piece == null || piece.Owner != currentPlayer) continue;
                var pos = new Point(c, r);
                captures.AddRange(CapturesFrom(pos, piece));
                simples.AddRange(SimplesFrom(pos, piece));
            }

        // Mandatory capture: if any capture exists, only captures are legal
        legalMoves = captures.Count > 0 ? captures : simples;
    }

    List<Move> CapturesFrom(Point pos, Piece piece)
    {
        var result = new List<Move>();

        if (piece.Type == PieceType.Man)
        {
            foreach (int dr in new[] { -1, 1 })
                foreach (int dc in new[] { -1, 1 })
                {
                    int er = pos.Y + dr,     ec = pos.X + dc;       // enemy square
                    int lr = pos.Y + 2 * dr, lc = pos.X + 2 * dc;  // landing square
                    if (!InBounds(lr, lc)) continue;
                    var enemy = board[er, ec];
                    if (enemy == null || enemy.Owner == piece.Owner) continue;
                    if (board[lr, lc] != null) continue;
                    result.Add(new Move(pos, new Point(lc, lr), new Point(ec, er)));
                }
        }
        else // King: scans the entire diagonal, jumps the first enemy found, lands anywhere beyond
        {
            foreach (int dr in new[] { -1, 1 })
                foreach (int dc in new[] { -1, 1 })
                {
                    int r = pos.Y + dr, c = pos.X + dc;
                    while (InBounds(r, c) && board[r, c] == null) { r += dr; c += dc; }
                    if (!InBounds(r, c) || board[r, c]!.Owner == piece.Owner) continue;

                    var capturedPos = new Point(c, r);
                    r += dr; c += dc;

                    while (InBounds(r, c) && board[r, c] == null)
                    {
                        result.Add(new Move(pos, new Point(c, r), capturedPos));
                        r += dr; c += dc;
                    }
                }
        }

        return result;
    }

    List<Move> SimplesFrom(Point pos, Piece piece)
    {
        var result = new List<Move>();

        if (piece.Type == PieceType.Man)
        {
            int forward = piece.Owner == Player.White ? -1 : 1;
            foreach (int dc in new[] { -1, 1 })
            {
                int nr = pos.Y + forward, nc = pos.X + dc;
                if (InBounds(nr, nc) && board[nr, nc] == null)
                    result.Add(new Move(pos, new Point(nc, nr)));
            }
        }
        else // King: any distance diagonally
        {
            foreach (int dr in new[] { -1, 1 })
                foreach (int dc in new[] { -1, 1 })
                {
                    int r = pos.Y + dr, c = pos.X + dc;
                    while (InBounds(r, c) && board[r, c] == null)
                    {
                        result.Add(new Move(pos, new Point(c, r)));
                        r += dr; c += dc;
                    }
                }
        }

        return result;
    }

    static bool InBounds(int r, int c) => r >= 0 && r < 8 && c >= 0 && c < 8;

    // ── Move execution ────────────────────────────────────────────────────────

    void ApplyMove(Move move)
    {
        var piece = board[move.From.Y, move.From.X]!;
        board[move.To.Y, move.To.X]     = piece;
        board[move.From.Y, move.From.X] = null;

        if (move.Captured.HasValue)
            board[move.Captured.Value.Y, move.Captured.Value.X] = null;

        // Promotion to King — also stops the multi-capture chain
        bool promoted = false;
        if (piece.Type == PieceType.Man)
        {
            if ((piece.Owner == Player.White && move.To.Y == 0) ||
                (piece.Owner == Player.Black && move.To.Y == 7))
            {
                piece.Type = PieceType.King;
                promoted   = true;
            }
        }

        // Continue multi-capture if further jumps are available
        if (move.IsCapture && !promoted)
        {
            var further = CapturesFrom(move.To, piece);
            if (further.Count > 0)
            {
                multiCaptureMode = true;
                selectedCell     = move.To;
                pieceMoves       = further;
                legalMoves       = further;
                Invalidate();
                return;
            }
        }

        EndTurn();
    }

    void EndTurn()
    {
        multiCaptureMode = false;
        selectedCell     = null;
        pieceMoves       = [];
        currentPlayer    = currentPlayer == Player.White ? Player.Black : Player.White;
        ComputeLegalMoves();

        if (legalMoves.Count == 0)
        {
            string winner = currentPlayer == Player.White ? "Czarny" : "Biały";
            string loser  = currentPlayer == Player.White ? "Białego" : "Czarnego";
            Invalidate();

            var answer = MessageBox.Show(
                $"Gracz {winner} wygrywa!\n\nGracz {loser} nie ma żadnych ruchów.\n\nCzy chcesz zagrać ponownie?",
                "Koniec gry",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (answer == DialogResult.Yes) StartNewGame();
            else                            Close();
            return;
        }

        Invalidate();
    }
}
