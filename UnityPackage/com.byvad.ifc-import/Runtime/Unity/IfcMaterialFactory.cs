// @author: Davy Bellens

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Conversion.Layers.Resource;

namespace Conversion.Unity
{
    /// <summary>
    /// Turns resolved IFC colours into URP materials, one per distinct colour.
    /// <para>
    /// A castle resolves to forty materials across three and a half thousand objects,
    /// so caching matters: without it every wall gets its own material and the SRP
    /// batcher has nothing to batch.
    /// </para>
    /// </summary>
    public sealed class IfcMaterialFactory
    {
        private const string UrpLitShader = "Universal Render Pipeline/Lit";

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Metallic = Shader.PropertyToID("_Metallic");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int Surface = Shader.PropertyToID("_Surface");
        private static readonly int Blend = Shader.PropertyToID("_Blend");
        private static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlend = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWrite = Shader.PropertyToID("_ZWrite");
        private static readonly int AlphaClip = Shader.PropertyToID("_AlphaClip");
        private static readonly int Cull = Shader.PropertyToID("_Cull");

        private readonly Dictionary<Rgba, Material> _cache = new Dictionary<Rgba, Material>();
        private readonly Shader _shader;
        private Material _fallback;

        /// <summary>
        /// Opacity floor. IFC glazing is routinely authored at Transparency 1.0,
        /// which resolves to alpha 0 — a window you cannot see at all. Anything
        /// below this is lifted to it.
        /// </summary>
        public float MinimumAlpha = 0.25f;

        /// <summary>Render both sides. IFC solids are not reliably closed, and a
        /// single-sided wall with an inverted face reads as a hole in the building.</summary>
        public bool DoubleSided = true;

        public IfcMaterialFactory(Shader shader = null)
        {
            _shader = shader != null ? shader : Shader.Find(UrpLitShader);
            if (_shader == null)
            {
                Debug.LogError(
                    $"[IFC] Shader '{UrpLitShader}' not found. This package targets URP; " +
                    "check that the Universal RP package is installed and a URP asset is " +
                    "assigned in Graphics settings.");
            }
        }

        public int Count => _cache.Count;

        public Material Fallback
        {
            get
            {
                if (_fallback == null)
                {
                    _fallback = Get(Appearance.UnstyledColour);
                    _fallback.name = Appearance.UnstyledName;
                }
                return _fallback;
            }
        }

        public Material Get(Rgba? colour)
        {
            if (!colour.HasValue)
            {
                return Fallback;
            }

            Rgba key = colour.Value;
            if (_cache.TryGetValue(key, out Material existing))
            {
                return existing;
            }

            Material material = Create(key);
            _cache[key] = material;
            return material;
        }

        /// <summary>Alpha at or above this reads as fully opaque — below it, the material goes transparent.</summary>
        private const float OpaqueAlphaThreshold = 0.999f;

        private Material Create(Rgba colour)
        {
            float alpha = ClampedAlpha(colour.A);

            var material = new Material(_shader)
            {
                name = MaterialName(colour, alpha),
                enableInstancing = true,
            };

            material.SetColor(BaseColor,
                new Color((float)colour.R, (float)colour.G, (float)colour.B, alpha));
            material.SetFloat(Metallic, 0f);
            material.SetFloat(Smoothness, alpha < OpaqueAlphaThreshold ? 0.85f : 0.25f);

            if (DoubleSided)
            {
                material.SetFloat(Cull, (float)CullMode.Off);
            }

            if (alpha < OpaqueAlphaThreshold)
            {
                MakeTransparent(material);
            }

            return material;
        }

        private float ClampedAlpha(double rawAlpha)
        {
            float alpha = (float)rawAlpha;
            return alpha < MinimumAlpha ? MinimumAlpha : alpha;
        }

        private static string MaterialName(Rgba colour, float alpha) =>
            new Rgba(colour.R, colour.G, colour.B, alpha).HexName();

        /// <summary>
        /// Switch a URP Lit material to transparent.
        /// <para>
        /// URP does not derive this from the alpha channel: the surface type is a
        /// material property with a matching set of shader keywords and blend states,
        /// and setting the colour alone leaves the material fully opaque. The
        /// inspector does all of this when you change the Surface Type dropdown;
        /// from script it has to be done by hand.
        /// </para>
        /// </summary>
        private static void MakeTransparent(Material material)
        {
            material.SetFloat(Surface, 1f);          // 0 opaque, 1 transparent
            material.SetFloat(Blend, 0f);            // 0 alpha blend
            material.SetFloat(SrcBlend, (float)BlendMode.SrcAlpha);
            material.SetFloat(DstBlend, (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat(ZWrite, 0f);
            material.SetFloat(AlphaClip, 0f);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            material.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}