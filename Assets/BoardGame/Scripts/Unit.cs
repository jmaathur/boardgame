using UnityEngine;

public class Unit
{
    public Player Owner { get; private set; }
    public IUnitDetails UnitDetails { get; private set; }
    public Vector2Int PlacementPosition { get; private set; }
    public Vector2Int CurrentPosition { get; set; }
    public GameObject VisualInstance { get; set; }

    public Unit(Player owner, IUnitDetails unitDetails, Vector2Int placementPosition)
    {
        Owner = owner;
        UnitDetails = unitDetails;
        PlacementPosition = placementPosition;
        CurrentPosition = placementPosition;
        VisualInstance = null;
    }
}
