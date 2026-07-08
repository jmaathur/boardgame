using UnityEngine;
using System.Collections.Generic;

public interface IUnitDetails
{
    string UnitName { get; }
    GameObject ModelPrefab { get; }
    Vector3 ModelPositionOffset { get; }
    Quaternion ModelRotation { get; }
    float ModelHeight { get; }
    Vector2Int FootprintSize { get; }

    // Returns local positions within the footprint where models should be spawned
    Vector3[] GetSquadFormation();
}
