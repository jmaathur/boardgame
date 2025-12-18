using UnityEngine;

// Recommended settings when creating the asset:
// - Footprint Size: (2, 1) - Whelps occupy 2 tiles width, 1 tile depth
// - Model Prefab: CrimsonFlight prefab
[CreateAssetMenu(fileName = "WhelpDetails", menuName = "Units/Whelp Details")]
public class WhelpDetails : BaseUnitDetails
{
    public override Vector3[] GetSquadFormation()
    {
        // 3 whelps in a V formation for flying units
        Vector3[] positions = new Vector3[3];

        positions[0] = new Vector3(0, 0.3f, 0.3f);    // Lead whelp, slightly elevated
        positions[1] = new Vector3(-0.4f, 0.2f, 0);   // Left wing
        positions[2] = new Vector3(0.4f, 0.2f, 0);    // Right wing

        return positions;
    }
}
