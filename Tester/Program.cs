﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using FaceClassifier;
using FaceClassifier.Normalisation;
using FaceDetection;

namespace Tester
{
	class Program
	{
		static void Main(string[] args)
		{
			var caltechWebFacesSourceImageFolder = new DirectoryInfo(@"CaltechWebFaces");
			if (!caltechWebFacesSourceImageFolder.Exists)
				throw new Exception("The \"CaltechWebFaces\" folder must exist alongside the binary and be populated with the images from http://www.vision.caltech.edu/Image_Datasets/Caltech_10K_WebFaces/");
			var groundTruthTextFile = new FileInfo(Path.Combine(caltechWebFacesSourceImageFolder.FullName, "WebFaces_GroundThruth.txt"));
			if (!caltechWebFacesSourceImageFolder.Exists)
				throw new Exception("The \"WebFaces_GroundThruth.txt\" file must exist in the CaltechWebFaces folder, it may be downloaded from http://www.vision.caltech.edu/Image_Datasets/Caltech_10K_WebFaces/");

			var faceDetectionResultsFolder = new DirectoryInfo("Results");
			EmptyAndDeleteFolder(faceDetectionResultsFolder);
			faceDetectionResultsFolder.Create();

			const int sampleWidth = 128;
			const int sampleHeight = sampleWidth;
			const int blockSize = 8;
			Normaliser normaliser = new OverlappingBlockwiseNormaliser(blockSize: 2).Normalise;
			const int minimumNumberOfImagesToTrainWith = 2000;

			var timer = new IntervalTimer(Console.WriteLine);
			var faceClassifier = CalTechWebFacesSvmTrainer.TrainFromCaltechData(caltechWebFacesSourceImageFolder, groundTruthTextFile, sampleWidth, sampleHeight, blockSize, minimumNumberOfImagesToTrainWith, normaliser, timer.Log);

			var faceDetector = new FaceDetector(DefaultConfiguration.Instance, timer.Log);
			var possibleFaceRegionsInImages = new[]
				{
					"TigerWoods.gif"
				}
				.Select(filePath => new FileInfo(filePath))
				.Select(file => new
				{
					File = file,
					PossibleFaceImages = ExtractPossibleFaceRegionsFromImage(file, faceDetector, (double)sampleWidth / sampleHeight, timer.Log).ToArray() // Prevent any potential repeated evaluation later on
				})
				.Select(fileAndPossibleFaceRegions => new
				{
					File = fileAndPossibleFaceRegions.File,
					PossibleFaceImages = fileAndPossibleFaceRegions.PossibleFaceImages
						.Select(possibleFaceImage => new
						{
							Image = possibleFaceImage.ExtractedImage,
							RegionInSource = possibleFaceImage.RegionInSource,
							IsFace = faceClassifier.IsFace(possibleFaceImage.ExtractedImage)
						})
				})
				.Select((fileAndPossibleFaceRegions, index) => 
				{
					// This will save the positive and negative matches into individual files in faceDetectionResultsFolder
					var possibleFaceRegions = fileAndPossibleFaceRegions.PossibleFaceImages.ToArray(); // Prevent repeated evaluation below
					var faceIndex = 0;
					var negativeIndex = 0;
					foreach (var possibleFaceRegion in possibleFaceRegions)
					{
						string filename;
						if (possibleFaceRegion.IsFace)
						{
							filename = "FACE_" + index + "_" + faceIndex;
							faceIndex++;
						}
						else
						{
							filename = "NEG_" + index + "_" + negativeIndex;
							negativeIndex++;
						}
						filename += "-" + fileAndPossibleFaceRegions.File.Name + ".png";
						possibleFaceRegion.Image.Save(Path.Combine(faceDetectionResultsFolder.FullName, filename));
						possibleFaceRegion.Image.Dispose();
					}
					return new
					{
						File = fileAndPossibleFaceRegions.File,
						PossibleFaceImages = possibleFaceRegions
							.Select(possibleFaceImage => new
							{
								RegionInSource = possibleFaceImage.RegionInSource,
								IsFace = possibleFaceImage.IsFace
							})

					};
				})
				.Select(fileAndPossibleFaceRegions =>
				{
					// This will save a copy of the original image to the faceDetectionResultsFolder with the detected faces outlined
					using (var source = new Bitmap(fileAndPossibleFaceRegions.File.FullName))
					{
						WriteOutputFile(
							Path.Combine(faceDetectionResultsFolder.FullName, fileAndPossibleFaceRegions.File.Name) + ".png",
							source,
							fileAndPossibleFaceRegions.PossibleFaceImages.Where(possibleFaceRegion => possibleFaceRegion.IsFace).Select(possibleFaceRegion => possibleFaceRegion.RegionInSource),
							Color.GreenYellow
						);
					}
					return fileAndPossibleFaceRegions;
				})
				.ToArray(); // Evaluate the above work

			Console.WriteLine();
			Console.WriteLine($"Identified {possibleFaceRegionsInImages.Sum(file => file.PossibleFaceImages.Count())} possible face region(s) in the first pass");
			Console.WriteLine($"{possibleFaceRegionsInImages.Sum(file => file.PossibleFaceImages.Count(possibleFace => possibleFace.IsFace))} of these was determined to be a face by the SVM filter");
			Console.WriteLine("The extracted regions may be seen in the " + faceDetectionResultsFolder.Name + " folder");
			Console.WriteLine();
			Console.WriteLine("Press [Enter] to terminate..");
			Console.ReadLine();
		}

		private static IEnumerable<PossibleFaceRegion> ExtractPossibleFaceRegionsFromImage(FileInfo file, ILookForPossibleFaceRegions faceDetector, double idealImageAspectRatio, Action<string> logger)
		{
			if (file == null)
				throw new ArgumentNullException(nameof(file));
			if (faceDetector == null)
				throw new ArgumentNullException(nameof(faceDetector));
			if (idealImageAspectRatio <= 0)
				throw new ArgumentOutOfRangeException(nameof(idealImageAspectRatio), "must be greater than zero");
			if (logger == null)
				throw new ArgumentNullException(nameof(logger));

			var config = DefaultConfiguration.Instance;
			using (var source = new Bitmap(file.FullName))
			{
				foreach (var faceRegion in faceDetector.GetPossibleFaceRegions(source))
				{
					Rectangle faceRegionAtIdealAspectRatio;
					var regionAspectRatio = (double)faceRegion.Width / faceRegion.Height;
					if (regionAspectRatio < idealImageAspectRatio)
					{
						// Face region is narrower than the aspect ratio that we want extracted sub-images to be for the next processing step, so try to expand its width
						var idealWidth = (int)(faceRegion.Height / idealImageAspectRatio);
						var amountToAddEitherSide = (idealWidth - faceRegion.Width) / 2;
						faceRegionAtIdealAspectRatio = Rectangle.FromLTRB(
							left: Math.Max(0, faceRegion.Left - amountToAddEitherSide),
							right: Math.Min(source.Width, faceRegion.Right + amountToAddEitherSide),
							top: faceRegion.Top,
							bottom: faceRegion.Bottom
						);
					}
					else
					{
						// Face region is wider than the aspect ratio that we want extracted sub-images to be for the next processing step, so try to expand its height
						var idealHeight = (int)(faceRegion.Width / idealImageAspectRatio);
						var amountToAddEitherSide = (idealHeight - faceRegion.Height) / 2;
						faceRegionAtIdealAspectRatio = Rectangle.FromLTRB(
							left: faceRegion.Left,
							right: faceRegion.Right,
							top: Math.Max(0, faceRegion.Top - amountToAddEitherSide),
							bottom: Math.Min(source.Height, faceRegion.Bottom + amountToAddEitherSide)
						);
					}
					yield return new PossibleFaceRegion(
						source.Clone(faceRegionAtIdealAspectRatio, source.PixelFormat), // Extract a sub-image based upon the detected region but matching the desired aspect ratio (as close as possible)
						faceRegion, // Report the actual detected face region in the result
						idealImageAspectRatio
					);
				}
			}
		}

		private sealed class PossibleFaceRegion
		{
			public PossibleFaceRegion(Bitmap extractedImage, Rectangle regionInSource, double idealImageAspectRatio)
			{
				if (extractedImage == null)
					throw new ArgumentNullException(nameof(extractedImage));
				if ((extractedImage.Width <= 0) || (extractedImage.Height <= 0))
					throw new ArgumentException($"Specified {nameof(extractedImage)} contains no pixels");
				if ((regionInSource.Width <= 0) || (regionInSource.Height <= 0))
					throw new ArgumentException($"Specified {nameof(regionInSource)} dimensions are invalid, at least one is zero or less");
				if (idealImageAspectRatio <= 0)
					throw new ArgumentOutOfRangeException(nameof(idealImageAspectRatio), "must be greater than zero");

				ExtractedImage = extractedImage;
				RegionInSource = regionInSource;
				IdealImageAspectRatio = idealImageAspectRatio;
			}

			/// <summary>
			/// The dimensions of this image may not match the dimensions of RegionInSource as the sub-image extracted for further processing should match the specified
			/// IdealImageAspectRatio as closely as possible (so the ExtractedImage may be wider than the detected RegionInSource if the detected region was narrower
			/// than the IdealImageAspectRatio specifies)
			/// </summary>
			public Bitmap ExtractedImage { get; }
			public Rectangle RegionInSource { get; }
			public double IdealImageAspectRatio { get; }
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
					if (faceRegions.Any())
					{
						using (var pen = new Pen(outline, width: 1))
						{
							g.DrawRectangles(pen, faceRegions.ToArray());
						}
					}
				}
				annotatedBitMap.Save(outputFilename);
			}
		}

		private static void EmptyAndDeleteFolder(DirectoryInfo folder)
		{
			while (folder.Exists)
			{
				try
				{
					folder.Delete(recursive: true);
				}
				catch { }
				folder.Refresh();
				System.Threading.Thread.Sleep(TimeSpan.FromSeconds(1));
			}
		}
	}
}