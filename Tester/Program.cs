﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using FaceDetection;

namespace Tester
{
	class Program
	{
		static void Main(string[] args)
		{
			var filename = "TigerWoods.gif";
			var outputFilename = "Output.png";

			var config = DefaultConfiguration.Instance;
			var timer = new IntervalTimer(Console.WriteLine);
			var faceDetector = new FaceDetector(config, timer.Log);
			using (var source = new Bitmap(filename))
			{
				var faceRegions = faceDetector.GetPossibleFaceRegions(source);
				WriteOutputFile(outputFilename, source, faceRegions, Color.GreenYellow);
				timer.Log($"Complete (written to {outputFilename}), {faceRegions.Count()} region(s) identified");
			}
			Console.WriteLine();
			Console.WriteLine("Press [Enter] to terminate..");
			Console.ReadLine();
		}

		private static void WriteOutputFile(string outputFilename, Bitmap source, IEnumerable<Rectangle> faceRegions, Color outline)
		{
			if (string.IsNullOrWhiteSpace(outputFilename))
				throw new ArgumentException($"Null/blank {nameof(outputFilename)} specified");
			if (source == null)
				throw new ArgumentNullException(nameof(source));
			if (faceRegions == null)
				throw new ArgumentNullException(nameof(faceRegions));

			// If the original image uses a palette (ie. an indexed PixelFormat) then GDI+ can't draw rectangle on it so we'll just create a fresh bitmap every time to
			// be on the safe side
			using (var annotatedBitMap = new Bitmap(source.Width, source.Height))
			{
				using (var g = Graphics.FromImage(annotatedBitMap))
				{
					g.DrawImage(source, 0, 0);
					using (var pen = new Pen(outline, width: 1))
					{
						g.DrawRectangles(pen, faceRegions.ToArray());
					}
				}
				annotatedBitMap.Save(outputFilename);
			}
		}
	}
}
