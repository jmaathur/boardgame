using System;

namespace BoardGame.Net
{
    /// <summary>
    /// C# mirror of the wire protocol defined in core/types/src/protocol.ts.
    /// Keep the two files in sync: messages are JSON objects sent as WebSocket
    /// text frames, discriminated by their "type" field.
    ///
    /// Board convention (matches BoardManager/BoardTile): "row" is the world-X
    /// tile index (the wide/lateral axis) and "col" is the world-Z tile index
    /// (the deep axis). The AUTHORITATIVE board size is whatever the server
    /// sends in <see cref="GameStateDto.board"/> (catalog-driven, currently
    /// 32x48); it is not a client constant.
    /// </summary>
    public static class GameProtocol
    {
        // Pre-connect fallback ONLY. The live board dimensions arrive with the
        // first welcome/state broadcast (GameStateDto.board) and BoardManager
        // builds its grid from those, not from these constants. Kept so any
        // pre-connection UI has a sane default; do not treat as truth.
        public const int BoardWidth = 32;
        public const int BoardHeight = 48;

        public const string UnitArcher = "archer";
        public const string UnitWhelp = "whelp";
        public const string UnitFootman = "footman";
        public const string UnitHolyKnight = "holyKnight";
        public const string UnitBarracks = "barracks";
        public const string UnitCathedral = "cathedral";

        /// <summary>
        /// Fallback bounds check against the pre-connect default size. Once a
        /// state broadcast has arrived, validate against the live board
        /// dimensions instead (the server is the real authority — it rejects
        /// out-of-bounds placements and simply sends no state change).
        /// </summary>
        public static bool IsInBounds(int row, int col)
        {
            return row >= 0 && row < BoardWidth && col >= 0 && col < BoardHeight;
        }
    }

    // ------------------------------------------------------------------
    // Client -> server messages
    // ------------------------------------------------------------------

    [Serializable]
    public class JoinMessage
    {
        public string type = "join";
        public string roomId;
        public string playerName;
    }

    [Serializable]
    public class PlaceUnitMessage
    {
        public string type = "placeUnit";
        public string unitType;
        public int row;
        public int col;
    }

    [Serializable]
    public class MoveUnitMessage
    {
        public string type = "moveUnit";
        public string unitId;
        public int row;
        public int col;
    }

    [Serializable]
    public class PingMessage
    {
        public string type = "ping";
    }

    // ------------------------------------------------------------------
    // Server -> client messages
    // ------------------------------------------------------------------

    /// <summary>Used to read the "type" discriminator before a full parse.</summary>
    [Serializable]
    public class MessageTypeProbe
    {
        public string type;
    }

    [Serializable]
    public class BoardDto
    {
        public int width;
        public int height;
    }

    [Serializable]
    public class PlayerDto
    {
        public string id;
        public string name;
    }

    [Serializable]
    public class UnitDto
    {
        public string id;
        public string ownerId;
        public string unitType;
        public int row;
        public int col;
    }

    [Serializable]
    public class GameStateDto
    {
        public BoardDto board;
        public PlayerDto[] players;
        public UnitDto[] units;
    }

    [Serializable]
    public class WelcomeMessage
    {
        public string type;
        public string playerId;
        public string roomId;
        public GameStateDto state;
    }

    [Serializable]
    public class StateMessage
    {
        public string type;
        public GameStateDto state;
    }

    [Serializable]
    public class ErrorMessage
    {
        public string type;
        public string code;
        public string message;
    }
}
