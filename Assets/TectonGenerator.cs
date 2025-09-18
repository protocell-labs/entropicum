using System.Collections.Generic;
using UnityEngine;

public class TectonGenerator : MonoBehaviour
{
    [SerializeField] private GameObject tectonPrefab;

    [Header("Grid Settings")]
    [SerializeField] private int countX = 200;
    [SerializeField] private int countY = 20;
    [SerializeField] private int countZ = 200;

    [Header("Noise Settings")]
    [SerializeField] private float noiseScaleX = 0.2f;
    [SerializeField] private float noiseScaleY = 0.2f;
    [SerializeField] private float noiseScaleZ = 0.2f;
    [SerializeField, Range(0f, 1f)] private float noiseThreshold = 0.3f;
    [SerializeField] private Vector3 noiseOffset = Vector3.zero;
    [SerializeField] private bool useRandomNoiseOffset = false;
    [SerializeField] private int noiseSeed = 12345;

    [Header("Sector Noise Shifting")]
    [Tooltip("Enable sector-based shifted noise sampling.")]
    [SerializeField] private bool enableSectorOffsets = true;

    [Tooltip("Number of grid cells along X that form one sector.")]
    [SerializeField, Min(1)] private int sectorSizeX = 20;
    [Tooltip("Number of grid cells along Y that form one sector.")]
    [SerializeField, Min(1)] private int sectorSizeY = 5;
    [Tooltip("Number of grid cells along Z that form one sector.")]
    [SerializeField, Min(1)] private int sectorSizeZ = 20;

    [Tooltip("Offset applied to noise sampling per sector (in noise units).")]
    [SerializeField] private Vector3 sectorOffsetSize = new Vector3(50f, 50f, 50f);

    [Header("Spacing")]
    [SerializeField] private float gapX = 0.1f;
    [SerializeField] private float gapY = 0.1f;
    [SerializeField] private float gapZ = 0.1f;

    [Header("Placement Offsets")]
    [Tooltip("Manual grid offset in GRID CELLS (applied if Auto-center is OFF).")]
    [SerializeField] private int offsetX = 0;
    [SerializeField] private int offsetY = 0;
    [SerializeField] private int offsetZ = 0;

    [Header("Centering & Clearing")]
    [Tooltip("If ON, the grid is centered at world origin (0,0,0).")]
    [SerializeField] private bool autoCenterOnWorldOrigin = true;

    [Tooltip("If ON, skip elements within the given XZ radius of the grid center.")]
    [SerializeField] private bool carveCentralHole = true;

    [Tooltip("Hole radius in world units (meters). Y is ignored (cylindrical clearing).")]
    [SerializeField] private float holeRadius = 8f;

    [Header("Palette & Assignment")]
    [Tooltip("Material used mostly near the TOP of the structure.")]
    [SerializeField] private Material palette_color_1;
    [Tooltip("Material that appears RARELY anywhere.")]
    [SerializeField] private Material palette_color_2;
    [Tooltip("Material used mostly near the BOTTOM of the structure.")]
    [SerializeField] private Material palette_color_3;

    [Tooltip("Global small probability for palette_color_2 (0..1).")]
    [SerializeField, Range(0f, 1f)] private float rareMat2Probability = 0.05f;

    [Tooltip("Seed for reproducible material assignment.")]
    [SerializeField] private int materialSeed = 54321;

    [Header("Palette Bias")]
    [Tooltip("Curve for top/bottom bias. 1=linear, 2=stronger extremes.")]
    [SerializeField, Min(0.5f)] private float heightBias = 2.0f;

    [Header("Palette Cycling")]
    [Tooltip("Enable rotating (cycling) the three palette colors per element based on grid index.")]
    [SerializeField] private bool enablePaletteCycle = true;

    public enum PaletteCycleAxis { X, Y, Z }
    [Tooltip("Axis used to compute the cycle (i, j, or k).")]
    [SerializeField] private PaletteCycleAxis paletteCycleAxis = PaletteCycleAxis.X;

    [Tooltip("Cycle length for palette rotation (use 3 for three materials).")]
    [SerializeField, Min(1)] private int paletteCycleSize = 3;

    [Tooltip("Additional offset added to the cycle (in steps).")]
    [SerializeField] private int paletteCycleOffset = 0;

    [Header("Rotation Jitter")]
    [SerializeField] private bool enableRotationJitter = true;
    [Tooltip("Max absolute jitter (degrees). Keep ≤ 1 for subtle wobble.")]
    [SerializeField, Range(0f, 2f)] private float jitterMaxDegrees = 0.8f;
    [Tooltip("Seed for reproducible rotation jitter.")]
    [SerializeField] private int jitterSeed = 7777;

    [Header("Explosion")]
    [SerializeField] private bool enableExplosion = false;
    [Tooltip("Explosion center in world space.")]
    [SerializeField] private Vector3 explosionCenter = Vector3.zero;
    [Tooltip("Scales the explosion displacement/rotation.")]
    [SerializeField, Min(0f)] private float explosionStrength = 0f;
    [Tooltip("Exponent for distance falloff (2 = inverse-square).")]
    [SerializeField, Min(1f)] private float explosionFalloffPower = 2f;
    [Tooltip("If ON, uses cube falloff (1/d^3) instead of the power above.")]
    [SerializeField] private bool useCubicFalloff = false;
    [Tooltip("Maximum random rotation (degrees) before scaling by falloff.")]
    [SerializeField, Min(0f)] private float explosionRotationScale = 45f;
    [Tooltip("Seed for explosion rotation randomness.")]
    [SerializeField] private int explosionRotationSeed = 24680;
    [Tooltip("Max angular deviation for explosion direction (degrees).")]
    [SerializeField, Range(0f, 45f)] private float explosionDirectionJitterDegrees = 8f;
    [Tooltip("Random magnitude jitter as ±fraction of displacement (0..1).")]
    [SerializeField, Range(0f, 1f)] private float explosionMagnitudeJitter = 0.25f;

    [Header("Combining")]
    [SerializeField] private bool combineMeshes = true;
    [SerializeField] private bool addMeshCollider = true;
    [SerializeField] private bool destroySourcesAfterCombine = true;

    private Vector3 tectonSize;

    private void Start()
    {
        if (tectonPrefab == null)
        {
            Debug.LogError("Tecton prefab is not assigned in the Inspector!");
            return;
        }

        // Measure prefab dimensions
        Renderer prefabRenderer = tectonPrefab.GetComponentInChildren<Renderer>();
        tectonSize = prefabRenderer != null ? prefabRenderer.bounds.size : Vector3.one;

        // Cell sizes (prefab + gaps)
        float cellX = tectonSize.x + gapX;
        float cellY = tectonSize.y + gapY;
        float cellZ = tectonSize.z + gapZ;

        // Resolve noise offset (seeded or manual)
        Vector3 sampledNoiseOffset = noiseOffset;
        if (useRandomNoiseOffset)
        {
            var rng = new System.Random(noiseSeed);
            sampledNoiseOffset = new Vector3(
                (float)rng.NextDouble() * 10000f,
                (float)rng.NextDouble() * 10000f,
                (float)rng.NextDouble() * 10000f
            );
        }

        // Compute grid origin in WORLD space (center on world origin if requested)
        Vector3 halfExtents = new Vector3(
            (countX - 1) * cellX * 0.5f,
            (countY - 1) * cellY * 0.5f,
            (countZ - 1) * cellZ * 0.5f
        );

        Vector3 gridOriginWorld = autoCenterOnWorldOrigin
            ? -halfExtents
            : transform.position + new Vector3(offsetX * cellX, offsetY * cellY, offsetZ * cellZ);

        Vector3 clearingCenter = autoCenterOnWorldOrigin
            ? Vector3.zero
            : (gridOriginWorld + halfExtents);

        // RNGs (deterministic)
        var matRng = new System.Random(materialSeed);
        var jitterRng = new System.Random(jitterSeed);
        var explosionRng = new System.Random(explosionRotationSeed);

        // Generate blocks with noise filtering, hole carving, material assignment, and rotation jitter
        for (int i = 0; i < countX; i++)
        {
            for (int j = 0; j < countY; j++)
            {
                for (int k = 0; k < countZ; k++)
                {
                    // World position for this cell
                    Vector3 pos = new Vector3(
                        gridOriginWorld.x + i * cellX,
                        gridOriginWorld.y + j * cellY,
                        gridOriginWorld.z + k * cellZ
                    );

                    // Cylindrical clearing test (XZ only)
                    if (carveCentralHole)
                    {
                        float dx0 = pos.x - clearingCenter.x;
                        float dz0 = pos.z - clearingCenter.z;
                        if ((dx0 * dx0 + dz0 * dz0) < (holeRadius * holeRadius))
                            continue; // inside hole → skip
                    }

                    // --- Sector-based shifted noise sampling (unchanged from your current version) ---
                    Vector3 sectorShift = Vector3.zero;
                    if (enableSectorOffsets)
                    {
                        int sectorX = Mathf.FloorToInt(i / (float)sectorSizeX);
                        int sectorY = Mathf.FloorToInt(j / (float)sectorSizeY);
                        int sectorZ = Mathf.FloorToInt(k / (float)sectorSizeZ);

                        sectorShift = new Vector3(
                            sectorX * sectorOffsetSize.x,
                            sectorY * sectorOffsetSize.y,
                            sectorZ * sectorOffsetSize.z
                        );
                    }

                    // Noise filter
                    float n = Perlin3D.Noise(
                        i * noiseScaleX + sampledNoiseOffset.x + sectorShift.x,
                        j * noiseScaleY + sampledNoiseOffset.y + sectorShift.y,
                        k * noiseScaleZ + sampledNoiseOffset.z + sectorShift.z
                    );
                    float normalized = (n + 1f) * 0.5f;
                    if (normalized < noiseThreshold) continue;

                    // --- Palette cycle (unchanged) ---
                    Material[] basePal = { palette_color_1, palette_color_2, palette_color_3 };
                    Material[] cycledPal = basePal;
                    if (enablePaletteCycle && paletteCycleSize > 0)
                    {
                        int axisIndex =
                            (paletteCycleAxis == PaletteCycleAxis.X) ? i :
                            (paletteCycleAxis == PaletteCycleAxis.Y) ? j : k;

                        int shift = Mathf.Abs(axisIndex + paletteCycleOffset) % paletteCycleSize;

                        cycledPal = new Material[3];
                        cycledPal[0] = basePal[(0 + shift) % 3];
                        cycledPal[1] = basePal[(1 + shift) % 3];
                        cycledPal[2] = basePal[(2 + shift) % 3];
                    }

                    // --- Material assignment (row-index based & biased; same logic) ---
                    float jNorm = (countY <= 1) ? 1f : (float)j / (countY - 1);
                    float curved = Mathf.Pow(jNorm, heightBias);

                    float p2 = Mathf.Clamp01(rareMat2Probability);
                    float remain = 1f - p2;
                    float p1 = remain * curved;          // top bias
                    float p3 = remain * (1f - curved);   // bottom bias
                    if (j == 0) { p1 = 0f; p3 = remain; }
                    else if (j == countY - 1) { p1 = remain; p3 = 0f; }

                    float r = (float)matRng.NextDouble();
                    Material chosen;
                    // cycledPal[1] = rare slot, [0] = top-biased, [2] = bottom-biased
                    if (r < p2 && cycledPal[1] != null)
                        chosen = cycledPal[1];
                    else if (r < p2 + p1 && cycledPal[0] != null)
                        chosen = cycledPal[0];
                    else
                        chosen = cycledPal[2] != null ? cycledPal[2]
                                 : (cycledPal[0] != null ? cycledPal[0]
                                 : (cycledPal[1] != null ? cycledPal[1] : null));

                    // --- Rotation jitter (very small) ---
                    Quaternion rot = Quaternion.identity;
                    if (enableRotationJitter && jitterMaxDegrees > 0f)
                    {
                        float ry = (float)(jitterRng.NextDouble() * 2.0 - 1.0) * jitterMaxDegrees;
                        bool tiltX = jitterRng.Next(0, 2) == 0;
                        float rx = tiltX ? (float)(jitterRng.NextDouble() * 2.0 - 1.0) * jitterMaxDegrees : 0f;
                        float rz = !tiltX ? (float)(jitterRng.NextDouble() * 2.0 - 1.0) * jitterMaxDegrees : 0f;
                        rot = Quaternion.Euler(rx, ry, rz);
                    }

                    // --- EXPLOSION (NEW) ---
                    Vector3 explodedPos = pos;
                    Quaternion explodedRot = rot;

                    if (enableExplosion && explosionStrength > 0f)
                    {
                        // Vector from explosion center to element origin (world)
                        Vector3 fromCenter = pos - explosionCenter;
                        float dist = fromCenter.magnitude;

                        // Avoid division by 0 (or huge impulses)
                        const float eps = 1e-4f;
                        if (dist < eps) dist = eps;

                        float power = useCubicFalloff ? 3f : Mathf.Max(1f, explosionFalloffPower);
                        float falloff = explosionStrength / Mathf.Pow(dist, power);

                        // Base direction
                        Vector3 dir = fromCenter / dist;

                        // === Direction jitter (small tilt) ===
                        if (explosionDirectionJitterDegrees > 0f)
                        {
                            // random vector
                            Vector3 rand = new Vector3(
                                (float)explosionRng.NextDouble() * 2f - 1f,
                                (float)explosionRng.NextDouble() * 2f - 1f,
                                (float)explosionRng.NextDouble() * 2f - 1f
                            );
                            if (rand.sqrMagnitude < 1e-8f) rand = Vector3.up;

                            // axis roughly perpendicular to dir
                            Vector3 axis = Vector3.Cross(dir, rand);
                            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(dir, Vector3.up);

                            float ang = ((float)explosionRng.NextDouble() * 2f - 1f) * explosionDirectionJitterDegrees;
                            Quaternion tilt = Quaternion.AngleAxis(ang, axis.normalized);
                            dir = (tilt * dir).normalized;
                        }

                        // === Magnitude jitter (± percentage) ===
                        if (explosionMagnitudeJitter > 0f)
                        {
                            float magJ = 1f + ((float)explosionRng.NextDouble() * 2f - 1f) * explosionMagnitudeJitter;
                            if (magJ < 0f) magJ = 0f; // no negative displacement
                            falloff *= magJ;
                        }

                        // Translation along (jittered) direction
                        explodedPos = pos + dir * falloff;

                        // Extra random rotation scaled by same falloff (unchanged)
                        if (explosionRotationScale > 0f)
                        {
                            float rxE = (float)(explosionRng.NextDouble() * 2.0 - 1.0) * explosionRotationScale * falloff;
                            float ryE = (float)(explosionRng.NextDouble() * 2.0 - 1.0) * explosionRotationScale * falloff;
                            float rzE = (float)(explosionRng.NextDouble() * 2.0 - 1.0) * explosionRotationScale * falloff;
                            Quaternion qE = Quaternion.Euler(rxE, ryE, rzE);
                            explodedRot = qE * explodedRot;
                        }
                    }


                    // Spawn & parent, apply material
                    var go = Instantiate(tectonPrefab, explodedPos, explodedRot, transform);
                    if (chosen != null)
                    {
                        var mr = go.GetComponentInChildren<MeshRenderer>();
                        if (mr != null) mr.sharedMaterial = chosen;
                    }
                }
            }
        }

        if (combineMeshes)
        {
            CombineByMaterial();
        }
    }

    /// <summary>
    /// Combine all spawned child meshes into material groups.
    /// Marks combined objects Static and optionally adds MeshCollider.
    /// </summary>
    private void CombineByMaterial()
    {
        var parent = transform;
        var worldToLocal = parent.worldToLocalMatrix;

        // Group all child meshes by their (first) material
        var byMaterial = new Dictionary<Material, List<CombineInstance>>();
        var meshFilters = GetComponentsInChildren<MeshFilter>(includeInactive: false);

        foreach (var mf in meshFilters)
        {
            if (mf.transform == transform) continue;
            if (mf.sharedMesh == null) continue;

            var mr = mf.GetComponent<MeshRenderer>();
            Material mat = (mr != null && mr.sharedMaterials != null && mr.sharedMaterials.Length > 0)
                ? mr.sharedMaterials[0]
                : null;

            if (mat == null) continue;

            if (!byMaterial.TryGetValue(mat, out var list))
            {
                list = new List<CombineInstance>();
                byMaterial[mat] = list;
            }

            var ci = new CombineInstance
            {
                mesh = mf.sharedMesh,
                transform = worldToLocal * mf.transform.localToWorldMatrix,
                subMeshIndex = 0
            };
            list.Add(ci);
        }

        if (byMaterial.Count == 0)
        {
            Debug.LogWarning("TectonGenerator: nothing to combine (no materials found).");
            return;
        }

        // Clean previous combined containers
        var previous = new List<GameObject>();
        foreach (Transform child in transform)
        {
            if (child.name.StartsWith("Combined_", System.StringComparison.Ordinal))
                previous.Add(child.gameObject);
        }
        foreach (var go in previous) Destroy(go);

        // Create combined GameObjects per material
        int totalCombinedParts = 0;
        int totalVertices = 0; // NEW

        foreach (var kvp in byMaterial)
        {
            var mat = kvp.Key;
            var combines = kvp.Value;
            if (combines.Count == 0) continue;

            string safeMatName = mat != null ? mat.name : "UnknownMat";
            var combinedGO = new GameObject($"Combined_{safeMatName}");
            combinedGO.transform.SetParent(transform, false);

            // Mark STATIC (cheaper rendering)
            combinedGO.isStatic = true;

            var mfCombined = combinedGO.AddComponent<MeshFilter>();
            var mrCombined = combinedGO.AddComponent<MeshRenderer>();

            var combinedMesh = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            combinedMesh.CombineMeshes(combines.ToArray(), mergeSubMeshes: true, useMatrices: true, hasLightmapData: false);

            mfCombined.sharedMesh = combinedMesh;
            if (mat != null) mrCombined.sharedMaterial = mat;

            if (addMeshCollider)
            {
                var col = combinedGO.AddComponent<MeshCollider>();
                col.sharedMesh = combinedMesh;
            }

            totalCombinedParts += combines.Count;
            totalVertices += combinedMesh.vertexCount; // NEW
        }

        if (destroySourcesAfterCombine)
        {
            // Destroy original spawned children that are not combined containers
            var toDestroy = new List<GameObject>();
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith("Combined_", System.StringComparison.Ordinal)) continue;
                toDestroy.Add(child.gameObject);
            }
            foreach (var go in toDestroy) Destroy(go);
        }

        Debug.Log($"TectonGenerator: Combined {totalCombinedParts} parts into {byMaterial.Count} material groups (Static). " +
                  $"Total vertices across all combined meshes = {totalVertices}");
    }


    private void OnValidate()
    {
        countX = Mathf.Max(1, countX);
        countY = Mathf.Max(1, countY);
        countZ = Mathf.Max(1, countZ);
        noiseScaleX = Mathf.Max(0.0001f, noiseScaleX);
        noiseScaleY = Mathf.Max(0.0001f, noiseScaleY);
        noiseScaleZ = Mathf.Max(0.0001f, noiseScaleZ);
        gapX = Mathf.Max(0f, gapX);
        gapY = Mathf.Max(0f, gapY);
        gapZ = Mathf.Max(0f, gapZ);
        holeRadius = Mathf.Max(0f, holeRadius);
        rareMat2Probability = Mathf.Clamp01(rareMat2Probability);
        heightBias = Mathf.Max(0.5f, heightBias);
        jitterMaxDegrees = Mathf.Clamp(jitterMaxDegrees, 0f, 2f);

        // clamps for sector fields
        sectorSizeX = Mathf.Max(1, sectorSizeX);
        sectorSizeY = Mathf.Max(1, sectorSizeY);
        sectorSizeZ = Mathf.Max(1, sectorSizeZ);

        // clamps for palette cycle
        paletteCycleSize = Mathf.Max(1, paletteCycleSize);

        // sanity for explosion
        explosionFalloffPower = Mathf.Max(1f, explosionFalloffPower);
        explosionStrength = Mathf.Max(0f, explosionStrength);
        explosionRotationScale = Mathf.Max(0f, explosionRotationScale);
    }
}
