using System;

namespace TacticalSim.Core.Ballistics
{
    public interface IDragModel
    {
        /// <summary>
        /// Returns the Drag Coefficient (Cd) for a given Mach number.
        /// </summary>
        float GetDragCoefficient(float mach);
    }

    /// <summary>
    /// A simplified dynamic drag model that increases Cd near transonic speeds (Mach 0.8 - 1.2).
    /// </summary>
    public class StandardDragCurve : IDragModel
    {
        private readonly float _baseCd;

        public StandardDragCurve(float baseCd = 0.3f)
        {
            _baseCd = baseCd;
        }

        public float GetDragCoefficient(float mach)
        {
            // Simplified transonic drag rise
            if (mach < 0.8f)
            {
                return _baseCd;
            }
            else if (mach >= 0.8f && mach <= 1.2f)
            {
                // Peak drag at Mach 1.0, interpolating up and down
                float peakCd = _baseCd * 2.5f;
                if (mach <= 1.0f)
                {
                    float t = (mach - 0.8f) / 0.2f;
                    return Lerp(_baseCd, peakCd, t);
                }
                else
                {
                    float t = (mach - 1.0f) / 0.2f;
                    return Lerp(peakCd, _baseCd * 1.2f, t); // Remains slightly higher supersonically
                }
            }
            else
            {
                // Supersonic regime: gradually drops off but stays above base
                return MathF.Max(_baseCd, _baseCd * 1.2f - (mach - 1.2f) * 0.05f);
            }
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }
    }
}
