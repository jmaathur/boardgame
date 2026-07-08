using UnityEngine;

// Recommended settings when creating the asset:
// - Footprint Size: (2, 1) - Whelps occupy 2 tiles width, 1 tile depth
// - Model Prefab: CrimsonFlight prefab
[CreateAssetMenu(fileName = "WhelpDetails", menuName = "Units/Whelp Details")]
public class WhelpDetails : BaseUnitDetails
{
    public override Vector3[] GetSquadFormation()
    {
        // 5 whelps: 3 in front row, 2 in back row across 2 tiles
        Vector3[] positions = new Vector3[5];

        // Front row: 3 whelps spread across 2 tiles
        positions[0] = new Vector3(-0.1f, 0.2f, 0);       // Front left
        positions[1] = new Vector3(0.5f, 0.2f, 0);    // Front center
        positions[2] = new Vector3(1.1f, 0.2f, 0);    // Front right

        // Back row: 2 whelps, slightly elevated for flying formation
        positions[3] = new Vector3(0.2f, 0.3f, 0.4f);  // Back left, elevated
        positions[4] = new Vector3(0.8f, 0.3f, 0.4f);  // Back right, elevated

        return positions;
    }
}
