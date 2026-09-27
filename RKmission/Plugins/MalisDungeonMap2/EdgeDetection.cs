using SharpNav.Geometry;
using System.Collections.Generic;
using System.Linq;

namespace MalisDungeonMap2
{
    public class EdgeDetection
    {
        public List<Edge> Start(IEnumerable<Triangle3> triangles)
        {
            Dictionary<Edge, int> edgeCount = new Dictionary<Edge, int>();

            foreach (var triangle in triangles)
            {
                foreach (var edge in triangle.GetEdges())
                {
                    if (edgeCount.ContainsKey(edge))
                    {
                        edgeCount[edge]++;
                    }
                    else
                    {
                        edgeCount[edge] = 1;
                    }
                }
            }
            return edgeCount.Where(e => e.Value == 1).Select(e => e.Key).ToList();
        }
    }
}