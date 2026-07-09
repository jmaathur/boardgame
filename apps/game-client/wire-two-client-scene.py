#!/usr/bin/env python3
"""
Wire SampleScene for the two-client shared board (docs/two-client-shared-board-plan.md §3),
deterministically and idempotently, without opening the Unity Editor.

It performs the exact steps the manual Editor checklist describes:
  1. Adds a scene-root GameObject "Net" with a GameServerClient component
     (serverUrl ws://localhost:7777/ws, roomId lobby, playerName A, connectOnStart on).
  2. Wires _BoardManager's `client` field to that GameServerClient, and assigns the
     MineUnit / TheirsUnit materials to `mineMaterial` / `theirsMaterial`.
  3. Registers the new Transform in SceneRoots so it shows up in the hierarchy.

IMPORTANT: Close the Unity Editor before running this — the Editor holds the scene in
memory and will overwrite on-disk edits on its next save. After running, reopen the
project; Unity re-imports the scene and the wiring is already in place.

Run:  python3 apps/game-client/wire-two-client-scene.py
Safe to run twice: it detects prior wiring and refuses to duplicate.

NOTE: playerName defaults to "A". For the SECOND client instance, change the running
GameServerClient's Player Name to "B" in the Inspector (or duplicate with a distinct
name). Two clients in the same room need DISTINCT player names.
"""

import sys
from pathlib import Path

SCENE = Path(__file__).parent / "Assets" / "Scenes" / "SampleScene.unity"

# Fresh, collision-free fileIDs (highest existing object anchor was 2065756974).
NET_GO = 2065756975
NET_TRANSFORM = 2065756976
NET_CLIENT = 2065756977

# Component / asset GUIDs (from .meta files).
GAMESERVERCLIENT_GUID = "2c3cee8b1a764f39b48b65ef8cefe87e"
MINE_MAT_GUID = "6711111d9a5149618d1022910d0e3319"
THEIRS_MAT_GUID = "530c9bfea0de4706ac0b20e2a4f337de"

# The _BoardManager MonoBehaviour anchor (assigns the new fields onto it).
BOARDMANAGER_MB = "&1541877564"

NET_BLOCKS = f"""--- !u!1 &{NET_GO}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {NET_TRANSFORM}}}
  - component: {{fileID: {NET_CLIENT}}}
  m_Layer: 0
  m_Name: Net
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &{NET_TRANSFORM}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {NET_GO}}}
  serializedVersion: 2
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {{fileID: 0}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
--- !u!114 &{NET_CLIENT}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {NET_GO}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {GAMESERVERCLIENT_GUID}, type: 3}}
  m_Name:
  m_EditorClassIdentifier:
  serverUrl: ws://localhost:7777/ws
  roomId: lobby
  playerName: A
  connectOnStart: 1
"""

# Extra fields appended onto the _BoardManager MonoBehaviour block.
BOARDMANAGER_FIELDS = f"""  client: {{fileID: {NET_CLIENT}}}
  mineMaterial: {{fileID: 2100000, guid: {MINE_MAT_GUID}, type: 2}}
  theirsMaterial: {{fileID: 2100000, guid: {THEIRS_MAT_GUID}, type: 2}}
  mineColor: {{r: 0.2, g: 0.7, b: 0.65, a: 1}}
  theirsColor: {{r: 0.8, g: 0.35, b: 0.2, a: 1}}
"""


def main() -> int:
    text = SCENE.read_text()

    if f"&{NET_CLIENT}" in text or "m_Name: Net\n" in text or "client: {fileID:" in text:
        print("Scene already wired (found Net/GameServerClient or a client field). No change.")
        return 0

    lines = text.splitlines(keepends=True)

    # 1. Append the new field lines onto the _BoardManager MonoBehaviour block.
    #    The block starts at the line '--- !u!114 &1541877564' and ends at the
    #    next '--- ' document separator. Insert our fields just before that.
    bm_start = next(i for i, l in enumerate(lines) if l.startswith(f"--- !u!114 {BOARDMANAGER_MB}"))
    bm_end = next(i for i in range(bm_start + 1, len(lines)) if lines[i].startswith("--- "))
    lines.insert(bm_end, BOARDMANAGER_FIELDS)

    text = "".join(lines)

    # 2. Insert the Net GameObject/Transform/MonoBehaviour blocks before SceneRoots.
    roots_marker = "--- !u!1660057539 &9223372036854775807\n"
    if roots_marker not in text:
        print("ERROR: SceneRoots block not found — scene structure changed. Aborting.", file=sys.stderr)
        return 1
    text = text.replace(roots_marker, NET_BLOCKS + roots_marker)

    # 3. Register the Net Transform under SceneRoots m_Roots.
    roots_anchor = "  m_Roots:\n"
    idx = text.index(roots_marker)
    roots_pos = text.index(roots_anchor, idx) + len(roots_anchor)
    text = text[:roots_pos] + f"  - {{fileID: {NET_TRANSFORM}}}\n" + text[roots_pos:]

    SCENE.write_text(text)
    print("Wired SampleScene:")
    print(f"  + Net GameObject (fileID {NET_GO}) with GameServerClient (ws://localhost:7777/ws, room lobby, name A)")
    print(f"  + _BoardManager.client -> GameServerClient, mineMaterial/theirsMaterial assigned")
    print(f"  + Net Transform registered in SceneRoots")
    print("Reopen the project in Unity. For the 2nd client, set that instance's Player Name to 'B'.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
