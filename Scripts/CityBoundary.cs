﻿using System.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Mapbox.Unity.Map;
using Mapbox.Unity.MeshGeneration.Data;
using Mapbox.Unity.Map.TileProviders;

namespace LoadingTools
{
    public class CityBoundary : MonoBehaviour
    {
        public static void CreateBoundary()
        {
            GameObject boundary = new GameObject
            {
                name = "City Limits"
            };
            AbstractMap mapShape = boundary.AddComponent<AbstractMap>();
            AbstractTileProvider tileProvider = boundary.AddComponent<Manhattan>();
            mapShape.Options.extentOptions.extentType = MapExtentType.Custom;
            mapShape.TileProvider = tileProvider;
            mapShape.InitializeOnStart = false;
            mapShape.Options.scalingOptions.unityTileSize = 150;
            mapShape.Options.placementOptions.placementType = MapPlacementType.AtTileCenter;
            mapShape.Options.placementOptions.snapMapToZero = true;

            CreateCity(mapShape);
            boundary.transform.GetChild(0).gameObject.AddComponent<MeshCollider>();
            while (boundary.transform.childCount > 1)
            {
                boundary.transform.GetChild(1).gameObject.AddComponent<MeshCollider>();
                boundary.transform.GetChild(1).SetParent(boundary.transform.GetChild(0));
            }
            MeshOptimizer.CombineParentAndChildrenMeshes(boundary.transform.GetChild(0));
            foreach (Transform child in boundary.transform)
            {
                DestroyImmediate(child.GetComponent<MeshRenderer>());
                DestroyImmediate(child.GetComponent<UnityTile>());
            }

            MeshCollider meshCollider = boundary.transform.GetChild(0).GetComponent<MeshCollider>();
            Mesh mesh = meshCollider.sharedMesh;
            List<Vertex> east = new List<Vertex>();
            List<Vertex> west = new List<Vertex>();

            SplitVertices(GetVertices(mesh), ref east, ref west);

            /* Connect the identified vertices into triangles */
            List<int> tris = mesh.triangles.ToList();
            List<Vector3> meshVerts = mesh.vertices.ToList();
            int k = west.Count - 1;
            while (k > 1)
            {
                if (west[k].Convex && !west[k - 1].Convex)
                {
                    tris.Add(meshVerts.IndexOf(west[k--].Position));
                    tris.Add(meshVerts.IndexOf(west[k--].Position));
                    tris.Add(meshVerts.IndexOf(west[k].Position));
                }
                else { k++; }
            }
            k = 0;
            while (k < east.Count - 2)
            {
                if (east[k].Convex && !east[k + 1].Convex)
                {
                    tris.Add(meshVerts.IndexOf(east[k++].Position));
                    tris.Add(meshVerts.IndexOf(east[k++].Position));
                    tris.Add(meshVerts.IndexOf(east[k].Position));
                }
                else { k++; }
            }
            mesh.triangles = tris.ToArray();
            meshCollider.transform.GetComponent<MeshFilter>().sharedMesh = mesh;
            boundary.transform.GetChild(0).GetComponent<MeshCollider>().sharedMesh = mesh;

            DestroyImmediate(mapShape);
            DestroyImmediate(tileProvider);
        }

        private static void CreateCity(AbstractMap m)
        {
            if (m.MapVisualizer != null) { m.ResetMap(); }
            else { m.MapVisualizer = ScriptableObject.CreateInstance<MapVisualizer>(); }
            m.Initialize(new Mapbox.Utils.Vector2d(40.764170691358686f, -73.97670925665614f), 16);
        }

        struct Vertex
        {
            public Vertex(Vector3 position, bool convex)
            {
                Position = position;
                Convex = convex;
            }
            public Vector3 Position { get; set; }
            public bool Convex { get; set; }
        }

        private static List<Vector3> GetVertices(Mesh mesh)
        {
            var borders = EdgeHelpers.GetEdges(mesh.triangles, mesh.vertices).FindBoundary();
            List<Vector3> vertices = new List<Vector3>();

            for (int i = 0; i < borders.Count; i++)
            {
                vertices.Add(borders[i].v1);
                vertices.Add(borders[i].v2);
            }
            vertices = RemoveDuplicate(vertices);

            return vertices;
        }

        private static void SplitVertices(List<Vector3> vertices, ref List<Vertex> east, ref List<Vertex> west)
        {
            Vector3[] dir = {
                new Vector3(1,0,1),
                new Vector3(-1, 0, -1),
                new Vector3(-1, 0, 1),
                new Vector3(1, 0, -1) };

            for (int i = vertices.Count - 1; i >= 0; i--)
            {
                int westFlag = 0;
                int count = 0;
                for (int j = 0; j < dir.Length; j++)
                {
                    if (IsInside(vertices[i] + dir[j], vertices))
                    {
                        westFlag += dir[j].x > 0 ? 1 : 0;
                        count++;
                    }
                }

                if (count > 2) // Concave
                {
                    if (westFlag > 1)
                    {
                        west.Add(new Vertex(vertices[i], false));
                    }
                    else
                    {
                        east.Add(new Vertex(vertices[i], false));
                    }
                }
                else if (count < 2) // Convex
                {
                    if (westFlag > 0)
                    {
                        west.Add(new Vertex(vertices[i], true));
                    }
                    else
                    {
                        east.Add(new Vertex(vertices[i], true));
                    }
                }
            }
            vertices.Clear();
            east = SortVertices(east);
            west = SortVertices(west);
        }

        private static List<Vertex> SortVertices(List<Vertex> vertices)
        {
            float epsilon = 1e-5f;
            Vertex anchor = vertices[0];
            int n = 0;
            for (int j = 0; j < vertices.Count; j++)
            {
                if (anchor.Position.x - vertices[j].Position.x >= -epsilon && anchor.Position.z - vertices[j].Position.z >= -epsilon)
                {
                    anchor = vertices[j];
                    n = j;
                }
            }

            vertices.RemoveAt(n);
            vertices.Insert(0, anchor);

            for (int i = 0; i < vertices.Count - 1; i++)
            {
                anchor = vertices[i];
                Vertex next = vertices[i + 1];
                for (int j = i + 1; j < vertices.Count; j++)
                {
                    if (Vector3.Magnitude(vertices[j].Position - anchor.Position) - Vector3.Magnitude(next.Position - anchor.Position) < epsilon)
                    {
                        next = vertices[j];
                        n = j;
                    }
                }

                vertices.RemoveAt(n);
                vertices.Insert(i + 1, next);
            }
            return vertices;
        }

        private static bool IsInside(Vector3 point, List<Vector3> bounds)
        {
            bool[] crit = { false, false, false, false };

            for (int i = 0; i < bounds.Count; i++)
            {
                if (!crit[0] && point.x > bounds[i].x && point.z < bounds[i].z)
                {
                    crit[0] = true;
                    continue;
                }
                if (!crit[1] && point.x > bounds[i].x && point.z > bounds[i].z)
                {
                    crit[1] = true;
                    continue;
                }
                if (!crit[2] && point.x < bounds[i].x && point.z < bounds[i].z)
                {
                    crit[2] = true;
                    continue;
                }
                if (!crit[3] && point.x < bounds[i].x && point.z > bounds[i].z)
                {
                    crit[3] = true;
                    continue;
                }
            }

            return crit[0] && crit[1] && crit[2] && crit[3];
        }

        private static List<Vector3> RemoveDuplicate(List<Vector3> points)
        {
            for (int i = points.Count - 1; i > 0; i--)
            {
                for (int j = i - 1; j >= 0; j--)
                {
                    if (Vector3.Magnitude(points[i] - points[j]) < 0.1f)
                    {
                        points.RemoveAt(j);
                        i--;
                    }
                }
            }
            return points;
        }
    }
}

