using UnityEngine;

// Recommended settings when creating the asset:
// - Footprint Size: (3, 1) - Archers occupy 3 tiles width, 1 tile depth
// - Model Prefab: ArcherInTheMist prefab
[CreateAssetMenu(fileName = "ArcherDetails", menuName = "Units/Archer Details")]
public class ArcherDetails : BaseUnitDetails
{
    public override Vector3[] GetSquadFormation()
    {
        // 5 archers in a line formation spread across 3 tiles
        Vector3[] positions = new Vector3[5];
        float spacing = 0.5f;

        for (int i = 0; i < 5; i++)
        {
            float x = (i - 2) * spacing; // Center the formation (-2, -1, 0, 1, 2)
            positions[i] = new Vector3(x, 0, 0);
        }

        return positions;
    }
}
