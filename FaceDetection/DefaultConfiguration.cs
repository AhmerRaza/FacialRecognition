﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Common;

namespace FaceDetection
{
	public sealed class DefaultConfiguration : IExposeConfigurationOptions
	{
		public static IExposeConfigurationOptions Instance => new DefaultConfiguration();
		private DefaultConfiguration() { }

		public int TextureAmplitudeFirstPassSmoothenMultiplier { get { return 8; } }
		public int TextureAmplitudeSecondPassSmoothenMultiplier { get { return 12; } }

		public DataRectangle<IRgBy> IRgByCalculator(DataRectangle<RGB> values)
		{
			if (values == null)
				throw new ArgumentNullException(nameof(values));

			// See http://web.archive.org/web/20090723024922/http:/geocities.com/jaykapur/face.html
			Func<byte, double> L = x => (105 * Math.Log10(x + 1));
			return values.Transform(
				value => new IRgBy(
					rg: L(value.R) - L(value.G),
					by: L(value.B) - ((L(value.G) + L(value.R)) / 2),
					i: (L(value.R) + L(value.B) + L(value.G)) / 3
				)
			);
		}
		public int RgBySmoothenMultiplier { get { return 2; } }

		/// <summary>
		/// A first pass is made to try to create a skin mask, this filter dictates what pixels are acceptable for that pass (taking into account hue, saturation and
		/// texture amplitude)
		/// </summary>
		public bool SkinFilter(HueSaturation colour)
		{
			// Started with recommendations from http://web.archive.org/web/20090723024922/http:/geocities.com/jaykapur/face.html but tweaked them a little (annotated below)
			return (
					((colour.Hue >= 105) && (colour.Hue <= 120) && (colour.Saturation >= 10) && (colour.Saturation <= 60)) || // Reduced minimum hue slightly to allow some lighter tones
					((colour.Hue >= 120) && (colour.Hue <= 160) && (colour.Saturation >= 10) && (colour.Saturation <= 60)) ||
					((colour.Hue >= 160) && (colour.Hue <= 180) && (colour.Saturation >= 30) && (colour.Saturation <= 40)) // Reduced acceptable saturation so that strong yellow tones aren't as readibly recognised
				)
				&& (colour.TextureAmplitude <= 9); // Some photos seem to need to accept a higher text amplitude, particularly if the face is a relatively small part of the image
		}
		/// <summary>
		/// After the first skin mask pass, a number of subsequent passes (see NumberOfSkinMaskRelaxedExpansions) are made to expand the mask to include any nearby pixels
		/// using more relaxed criteria (to make it more likely that edge pixels that are in shade, for example, are captured)
		/// </summary>
		public bool RelaxedSkinFilter(HueSaturation colour)
		{
			// This is the same as described at http://web.archive.org/web/20090723024922/http:/geocities.com/jaykapur/face.html, which is the same as the article "Naked People Skin
			// Filter (Margaret M. Fleck and David A Forsyth)" that it references (https://www.cs.hmc.edu/~fleck/naked-skin.html)
			return (colour.Hue >= 110) && (colour.Hue <= 180) && (colour.Saturation >= 0) && (colour.Saturation <= 180);
		}
		public int NumberOfSkinMaskRelaxedExpansions { get { return 5; } }

		/// <summary>
		/// Some regions may be ignore outright if their aspect ratios seem wrong (a very long, narrow region is unlikely to be a meaninful face capture, for example)
		/// </summary>
		public IEnumerable<Rectangle> FaceRegionAspectRatioFilter(IEnumerable<Rectangle> areas)
		{
			if (areas == null)
				throw new ArgumentNullException(nameof(areas));

			// Only accept areas that seem like they are be a sensible aspect ratio
			var allowedAreas = new List<Rectangle>();
			foreach (var area in areas)
			{
				if ((area.Width <= 0) || (area.Height <= 0))
					throw new ArgumentException($"Encounted invalid {nameof(areas)} value (both dimensions must be positive)");
				var longestSideMultiple = (double)Math.Max(area.Width, area.Height) / Math.Min(area.Width, area.Height);
				if (longestSideMultiple > 2.4)
					continue;
				allowedAreas.Add(area);
			}

			// If there are any regions that overlap a lot then look for any obvious regions that may be removed (for example, sometimes there will be a good match over
			// most of a face but then a separate match that overlaps a lot - or entirely - but that is much smaller; in this case, the smaller region may be removed)
			foreach (var area in allowedAreas)
			{
				var areaOfThisArea = GetArea(area);
				var areasThatMakesThisOneObsolete = allowedAreas
					.Where(other => GetArea(other) > (areaOfThisArea * 2))
					.Where(other => { other.Intersect(area); return GetArea(other) > (0.75 * areaOfThisArea); });
				if (!areasThatMakesThisOneObsolete.Any())
					yield return area;
			}
		}

		public double PercentToExpandFinalFaceRegionBy { get { return 0.1; } }

		private static double GetArea(Rectangle area)
		{
			return area.Width * area.Height;
		}
	}
}
