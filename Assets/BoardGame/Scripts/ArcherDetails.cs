using UnityEngine;

// Recommended settings when creating the asset:
// - Footprint Size: (2, 2) - Archers occupy 2 tiles width, 2 tiles depth
// - Model Prefab: ArcherInTheMist prefab
[CreateAssetMenu(fileName = "ArcherDetails", menuName = "Units/Archer Details")]
public class ArcherDetails : BaseUnitDetails
{
    public override Vector3[] GetSquadFormation()
    {
        // 5 archers: 3 in front row, 2 in back row across 2 tiles
        Vector3[] positions = new Vector3[5];

        // Front row: 3 archers
        positions[0] = new Vector3(0, 0, 0);      // Front left
        positions[1] = new Vector3(0.5f, 0, 0);   // Front center
        positions[2] = new Vector3(1.0f, 0, 0);   // Front right

        // Back row: 2 archers (staggered)
        positions[3] = new Vector3(0.25f, 0, 0.5f);  // Back left
        positions[4] = new Vector3(0.75f, 0, 0.5f);  // Back right

        return positions;
    }
}
