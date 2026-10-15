using System.Collections.Generic;
using UnityEngine;

// Drop empty GameObjects in the scene as waypoints and either (a) parent them under this
// object and leave 'points' empty to auto-collect them in order, or (b) drag them into the
// 'points' list explicitly. Gizmos draw the route so you can see and tweak it in the editor.
public class AllyPath : MonoBehaviour
{
    [Tooltip("Ordered waypoints. If left empty, this object's direct children are used, top-to-bottom as ordered in the Hierarchy.")]
    [SerializeField] private List<Transform> points = new List<Transform>();

    public int Count => ResolvedPoints().Count;

    // World position of waypoint index (clamped to the valid range).
    public Vector3 GetPoint(int index)
    {
        var pts = ResolvedPoints();
        index = Mathf.Clamp(index, 0, pts.Count - 1);
        return pts[index].position;
    }

    // The waypoints in order: the explicit points list if filled in, otherwise this object's children.
    private List<Transform> ResolvedPoints()
    {
        if (points != null && points.Count > 0) return points;

        // Fall back to direct children in Hierarchy order.
        var childPts = new List<Transform>();
        foreach (Transform child in transform) childPts.Add(child);
        return childPts;
    }

    // Draws each waypoint and the lines between them in the Scene view.
    private void OnDrawGizmos()
    {
        var pts = ResolvedPoints();
        Gizmos.color = Color.green;

        for (int i = 0; i < pts.Count; i++)
        {
            if (pts[i] == null) continue;
            Gizmos.DrawWireSphere(pts[i].position, 0.4f);

            if (i + 1 < pts.Count && pts[i + 1] != null)
            {
                Gizmos.DrawLine(pts[i].position, pts[i + 1].position);
            }
        }
    }
}