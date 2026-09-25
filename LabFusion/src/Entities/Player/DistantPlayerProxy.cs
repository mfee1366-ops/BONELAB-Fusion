using Il2CppInterop.Runtime.InteropTypes.Arrays;

using LabFusion.Data;
using LabFusion.Marrow;
using LabFusion.Utilities;

using UnityEngine;

namespace LabFusion.Entities;

/// <summary>
/// VRChat-style floating diamond used in place of a remote player's avatar. It is shown when the
/// player's avatar is missing, downloading, or failed to load, and when the player is far away.
/// </summary>
internal sealed class DistantPlayerProxy
{
    private static readonly Color DiamondColor = new(0.1f, 0.45f, 1f, 1f);
    private static readonly Color DiamondEmission = new(0.02f, 0.12f, 0.35f, 1f);

    private const float SpinDegreesPerSecond = 60f;
    private const float BobAmplitude = 0.04f;
    private const float BobFrequency = 1.5f;

    private static Material _material;
    private static Mesh _mesh;

    private readonly GameObject _diamond;
    private readonly Transform _transform;
    private bool _visible;
    private float _time;

    private const float GlideSharpness = 8f;
    private const float MaxGlideDistanceSqr = 10f * 10f;

    private Vector3 _smoothedPelvis;
    private bool _hasSmoothedPelvis;

    internal DistantPlayerProxy(byte playerId)
    {
        _diamond = new GameObject($"Fusion Fallback Diamond {playerId}");
        _transform = _diamond.transform;

        _diamond.AddComponent<MeshFilter>().sharedMesh = GetMesh();

        var renderer = _diamond.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = GetMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        _diamond.SetActive(false);
    }

    internal bool IsValid => _diamond != null;

    /// <summary>
    /// Builds an octahedron with split vertices so every facet gets its own flat normal.
    /// </summary>
    private static Mesh GetMesh()
    {
        if (_mesh != null)
        {
            return _mesh;
        }

        var top = new Vector3(0f, 1f, 0f);
        var bottom = new Vector3(0f, -1f, 0f);
        var ring = new Vector3[]
        {
            new(1f, 0f, 0f), new(0f, 0f, 1f), new(-1f, 0f, 0f), new(0f, 0f, -1f),
        };

        // Clockwise winding when viewed from outside, which Unity treats as the front face
        var corners = new Vector3[24];

        for (var i = 0; i < 4; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % 4];
            int offset = i * 6;

            corners[offset] = top;
            corners[offset + 1] = b;
            corners[offset + 2] = a;

            corners[offset + 3] = bottom;
            corners[offset + 4] = a;
            corners[offset + 5] = b;
        }

        var vertices = new Il2CppStructArray<Vector3>(corners.Length);
        var triangles = new Il2CppStructArray<int>(corners.Length);

        for (var i = 0; i < corners.Length; i++)
        {
            vertices[i] = corners[i];
            triangles[i] = i;
        }

        _mesh = new Mesh { name = "Fusion Fallback Diamond", hideFlags = HideFlags.DontUnloadUnusedAsset };
        _mesh.vertices = vertices;
        _mesh.triangles = triangles;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        return _mesh;
    }

    private static Material GetMaterial()
    {
        if (_material != null)
        {
            return _material;
        }

        // Lit shaders show the facets. Unlit is the last resort, where the diamond reads as a flat silhouette.
        // A shader can exist but be stripped or unsupported in BONELAB's pipeline, which renders nothing,
        // so only accept supported ones. The local avatar's shader is known to render as a last resort.
        var shader = FindSupportedShader("Universal Render Pipeline/Simple Lit")
            ?? FindSupportedShader("Universal Render Pipeline/Lit")
            ?? FindSupportedShader("Universal Render Pipeline/Unlit")
            ?? FindSupportedShader("Standard")
            ?? FindRigShader()
            ?? Shader.Find("Hidden/InternalErrorShader");

        FusionLogger.Log($"Fallback diamond is using shader {(shader != null ? shader.name : "none")}.");

        _material = new Material(shader) { name = "Fusion Fallback Diamond", hideFlags = HideFlags.DontUnloadUnusedAsset };

        // URP reads _BaseColor while legacy and SLZ shaders read _Color, so set whichever exist.
        SetColorIfPresent(_material, "_BaseColor", DiamondColor);
        SetColorIfPresent(_material, "_Color", DiamondColor);
        SetTextureIfPresent(_material, "_BaseMap");
        SetTextureIfPresent(_material, "_MainTex");

        if (_material.HasProperty("_EmissionColor"))
        {
            _material.EnableKeyword("_EMISSION");
            _material.SetColor("_EmissionColor", DiamondEmission);
        }

        return _material;
    }

    private static Shader FindSupportedShader(string name)
    {
        var shader = Shader.Find(name);

        return shader != null && shader.isSupported ? shader : null;
    }

    private static Shader FindRigShader()
    {
        if (!RigData.HasPlayer)
        {
            return null;
        }

        // Unity objects must be null checked with ==, since ?. skips Unity's destroyed-object check
        var avatar = RigData.Refs.RigManager.avatar;

        if (avatar == null)
        {
            return null;
        }

        var renderer = avatar.GetComponentInChildren<SkinnedMeshRenderer>(true);

        return renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.shader : null;
    }

    private static void SetColorIfPresent(Material material, string property, Color color)
    {
        if (material.HasProperty(property))
        {
            material.SetColor(property, color);
        }
    }

    private static void SetTextureIfPresent(Material material, string property)
    {
        if (material.HasProperty(property))
        {
            material.SetTexture(property, Texture2D.whiteTexture);
        }
    }

    /// <param name="refs">The rig to follow.</param>
    /// <param name="height">The player's avatar height in meters, used to size the diamond.</param>
    internal void Update(RigRefs refs, float height, float deltaTime)
    {
        if (!_visible || !refs.IsValid)
        {
            return;
        }

        var pelvis = refs.RigManager.physicsRig.m_pelvis.position;
        var head = refs.Head.position;

        Place(Vector3.Lerp(pelvis, head, 0.6f), GetScale(height), deltaTime);
    }

    /// <summary>
    /// Places the diamond from a synced pelvis position, for players whose rig is culled.
    /// </summary>
    internal void UpdateFromPelvis(Vector3 pelvis, float height, float deltaTime)
    {
        if (!_visible)
        {
            return;
        }

        // Far players only update a couple of times per second, so glide between poses instead of
        // jumping. Large gaps (teleports, first appearance) snap straight to the new position.
        if (!_hasSmoothedPelvis || (pelvis - _smoothedPelvis).sqrMagnitude > MaxGlideDistanceSqr)
        {
            _smoothedPelvis = pelvis;
            _hasSmoothedPelvis = true;
        }
        else
        {
            _smoothedPelvis = Vector3.Lerp(_smoothedPelvis, pelvis, 1f - Mathf.Exp(-GlideSharpness * deltaTime));
        }

        float scale = GetScale(height);

        // Without a live rig, estimate the chest from the pelvis
        Place(_smoothedPelvis + new Vector3(0f, 0.35f * scale, 0f), scale, deltaTime);
    }

    private static float GetScale(float height)
    {
        return Mathf.Clamp(height / MarrowGameReferences.CalibrationAvatarHeight, 0.4f, 3f);
    }

    private void Place(Vector3 chest, float scale, float deltaTime)
    {
        _time += deltaTime;

        float bob = Mathf.Sin(_time * BobFrequency * 2f * Mathf.PI) * BobAmplitude * scale;

        _transform.SetPositionAndRotation(
            chest + new Vector3(0f, bob, 0f),
            Quaternion.Euler(0f, _time * SpinDegreesPerSecond, 0f));
        _transform.localScale = new Vector3(0.22f, 0.36f, 0.22f) * scale;
    }

    internal void SetVisible(bool visible)
    {
        if (_visible == visible || _diamond == null)
        {
            return;
        }

        _visible = visible;
        _hasSmoothedPelvis = false;
        _diamond.SetActive(visible);
    }

    internal void Destroy()
    {
        if (_diamond != null)
        {
            UnityEngine.Object.Destroy(_diamond);
        }
    }
}
