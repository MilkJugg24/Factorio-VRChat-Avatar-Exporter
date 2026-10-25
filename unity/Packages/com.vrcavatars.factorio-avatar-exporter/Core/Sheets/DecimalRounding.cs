using System;
using System.Numerics;

namespace FactorioAvatarExporter.Core
{
    /// <summary>Rounding to decimal places exactly like Python, so shifts match tools/dragon_render.</summary>
    public static class DecimalRounding
    {
        /// <summary>Most decimal places <see cref="Round"/> accepts (10^15 is still an exact double).</summary>
        public const int MaxDigits = 15;

        /// <summary>
        /// Python's <c>round(value, digits)</c>: the exact binary value of <paramref name="value"/> is
        /// rounded to <paramref name="digits"/> decimal places, ties to even, and the double nearest to
        /// that decimal number is returned. (Scaling by 10^digits in floating point, as
        /// <c>Math.Round</c> does, misrounds ties such as 0.015625.) The sign of zero is kept, so small
        /// negative values round to -0.0 like in Python; infinities and NaN are returned unchanged.
        /// </summary>
        public static double Round(double value, int digits)
        {
            if (digits < 0 || digits > MaxDigits)
            {
                throw new ArgumentOutOfRangeException(nameof(digits), "digits must be between 0 and " + MaxDigits);
            }
            if (value == 0 || double.IsNaN(value) || double.IsInfinity(value))
            {
                return value;
            }
            long bits = BitConverter.DoubleToInt64Bits(value);
            bool negative = bits < 0;
            int exponentBits = (int)((bits >> 52) & 0x7FF);
            long mantissa = bits & 0xFFFFFFFFFFFFFL;
            int exponent;
            if (exponentBits == 0)
            {
                exponent = -1074; // subnormal
            }
            else
            {
                mantissa |= 1L << 52;
                exponent = exponentBits - 1075;
            }
            // |value| = mantissa * 2^exponent. When the spacing of doubles at value (2^exponent) is
            // coarser than 10^-digits, the rounded decimal, at most half of 10^-digits away, is nearer
            // to value than to its neighbours, and value is the result. (Below a power of two the
            // spacing halves, but such powers are integers or would need more than 15 digits.)
            if (exponent >= 0)
            {
                return value;
            }
            BigInteger scale = BigInteger.Pow(10, digits);
            BigInteger denominator = BigInteger.One << -exponent;
            if (denominator < scale)
            {
                return value;
            }
            BigInteger quotient = BigInteger.DivRem(mantissa * scale, denominator, out BigInteger remainder);
            int half = (remainder * 2).CompareTo(denominator);
            if (half > 0 || (half == 0 && !quotient.IsEven))
            {
                quotient += 1;
            }
            // quotient <= 2^53 here, so both conversions are exact and the division rounds once,
            // giving the double nearest to quotient / 10^digits (what Python's strtod returns).
            double result = (double)quotient / (double)scale;
            return negative ? -result : result;
        }
    }
}
