using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.WindowsForms;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using GMap.NET.MapProviders;
using System.IO.Compression;
using System.Text;
using System.Globalization;
using MyMap.Forms;

namespace Demo.WindowsForms
{
   public partial class StaticImage : Form
   {
      MainMapForm Main;

      BackgroundWorker bg = new BackgroundWorker();
      readonly List<GPoint> _tileArea = new List<GPoint>();

      public StaticImage(MainMapForm main)
      {
         InitializeComponent();

         Main = main;

         numericUpDown1.Maximum = Main.MainMap.MaxZoom;
         numericUpDown1.Minimum = Main.MainMap.MinZoom;
         numericUpDown1.Value = new decimal(Main.MainMap.Zoom);

         bg.WorkerReportsProgress = true;
         bg.WorkerSupportsCancellation = true;
         bg.DoWork += bg_DoWork;
         bg.ProgressChanged += bg_ProgressChanged;
         bg.RunWorkerCompleted += bg_RunWorkerCompleted;
      }

      void bg_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
      {
         if(!e.Cancelled)
         {
            if(e.Error != null)
            {
               MessageBox.Show("Error:" + e.Error.ToString(), "GMap.NET", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else if(e.Result != null)
            {
               try
               {
                  Process.Start(e.Result as string);
               }
               catch
               {
               }
            }
         }

         Text = "Static Map maker";
         progressBar1.Value = 0;
         button1.Enabled = true;
         numericUpDown1.Enabled = true;
         Main.MainMap.Refresh();
      }

      void bg_ProgressChanged(object sender, ProgressChangedEventArgs e)
      {
         progressBar1.Value = e.ProgressPercentage;

         var p = (GPoint)e.UserState;
         Text = "Static Map maker: Downloading[" + p + "]: " + _tileArea.IndexOf(p) + " of " + _tileArea.Count;
      }

      void bg_DoWork(object sender, DoWorkEventArgs e)
      {
         var info = (MapInfo)e.Argument;
         if(!info.Area.IsEmpty)
         {
            string bigImage = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) + Path.DirectorySeparatorChar + "GMap at zoom " + info.Zoom + " - " + info.Type + "-" + DateTime.Now.Ticks + ".jpg";
            e.Result = bigImage;

            // current area
            var topLeftPx = info.Type.Projection.FromLatLngToPixel(info.Area.LocationTopLeft, info.Zoom);
            var rightButtomPx = info.Type.Projection.FromLatLngToPixel(info.Area.Bottom, info.Area.Right, info.Zoom);
            var pxDelta = new GPoint(rightButtomPx.X - topLeftPx.X, rightButtomPx.Y - topLeftPx.Y);
            var maxOfTiles = info.Type.Projection.GetTileMatrixMaxXY(info.Zoom);

            int padding = info.MakeWorldFile || info.MakeKmz ? 0 : 22;
            {
               using(var bmpDestination = new Bitmap((int)(pxDelta.X + padding * 2), (int)(pxDelta.Y + padding * 2)))
               {
                  using(var gfx = Graphics.FromImage(bmpDestination))
                  {
                     gfx.InterpolationMode = InterpolationMode.HighQualityBicubic;
                     gfx.SmoothingMode = SmoothingMode.HighQuality;

                     int i = 0;

                     // get tiles & combine into one
                     lock(_tileArea)
                     {
                        foreach(var p in _tileArea)
                        {
                           if(bg.CancellationPending)
                           {
                              e.Cancel = true;
                              return;
                           }

                           int pc = (int)(((double)++i / _tileArea.Count) * 100);
                           bg.ReportProgress(pc, p);

                           foreach(var tp in info.Type.Overlays)
                           {
                              Exception ex;
                              GMapImage tile;

                              // tile number inversion(BottomLeft -> TopLeft) for pergo maps
                              if(tp.InvertedAxisY)
                              {
                                 tile = GMaps.Instance.GetImageFrom(tp, new GPoint(p.X, maxOfTiles.Height - p.Y), info.Zoom, out ex) as GMapImage;
                              }
                              else // ok
                              {
                                 tile = GMaps.Instance.GetImageFrom(tp, p, info.Zoom, out ex) as GMapImage;
                              }

                              if(tile != null)
                              {
                                 using(tile)
                                 {
                                    long x = p.X * info.Type.Projection.TileSize.Width - topLeftPx.X + padding;
                                    long y = p.Y * info.Type.Projection.TileSize.Width - topLeftPx.Y + padding;
                                    {
                                       gfx.DrawImage(tile.Img, x, y, info.Type.Projection.TileSize.Width, info.Type.Projection.TileSize.Height);
                                    }
                                 }
                              }
                           }
                        }
                     }

                     // draw routes
                     {
                        foreach(var r in Main.Routes.Routes)
                        {
                           if(r.IsVisible)
                           {
                              using(var rp = new GraphicsPath())
                              {
                                 for(int j = 0; j < r.Points.Count; j++)
                                 {
                                    var pr = r.Points[j];
                                    var px = info.Type.Projection.FromLatLngToPixel(pr.Lat, pr.Lng, info.Zoom);

                                    px.Offset(padding, padding);
                                    px.Offset(-topLeftPx.X, -topLeftPx.Y);

                                    var p2 = px;

                                    if(j == 0)
                                    {
                                       rp.AddLine(p2.X, p2.Y, p2.X, p2.Y);
                                    }
                                    else
                                    {
                                       var p = rp.GetLastPoint();
                                       rp.AddLine(p.X, p.Y, p2.X, p2.Y);
                                    }
                                 }

                                 if(rp.PointCount > 0)
                                 {
                                    gfx.DrawPath(r.Stroke, rp);
                                 }
                              }
                           }
                        }
                     }

                     // draw polygons
                     {
                        foreach(var r in Main.Polygons.Polygons)
                        {
                           if(r.IsVisible)
                           {
                              using(var rp = new GraphicsPath())
                              {
                                 for(int j = 0; j < r.Points.Count; j++)
                                 {
                                    var pr = r.Points[j];
                                    var px = info.Type.Projection.FromLatLngToPixel(pr.Lat, pr.Lng, info.Zoom);

                                    px.Offset(padding, padding);
                                    px.Offset(-topLeftPx.X, -topLeftPx.Y);

                                    var p2 = px;

                                    if(j == 0)
                                    {
                                       rp.AddLine(p2.X, p2.Y, p2.X, p2.Y);
                                    }
                                    else
                                    {
                                       var p = rp.GetLastPoint();
                                       rp.AddLine(p.X, p.Y, p2.X, p2.Y);
                                    }
                                 }

                                 if(rp.PointCount > 0)
                                 {
                                    rp.CloseFigure();

                                    gfx.FillPath(r.Fill, rp);

                                    gfx.DrawPath(r.Stroke, rp);
                                 }
                              }
                           }
                        }
                     }

                     // draw markers
                     {
                        foreach(var r in Main.Objects.Markers)
                        {
                           if(r.IsVisible)
                           {
                              var pr = r.Position;
                              var px = info.Type.Projection.FromLatLngToPixel(pr.Lat, pr.Lng, info.Zoom);

                              px.Offset(padding, padding);
                              px.Offset(-topLeftPx.X, -topLeftPx.Y);
                              px.Offset(r.Offset.X, r.Offset.Y);

                              gfx.ResetTransform();
                              gfx.TranslateTransform(-r.LocalPosition.X, -r.LocalPosition.Y);
                              gfx.TranslateTransform((int)px.X, (int)px.Y);

                              r.OnRender(gfx);
                           }
                        }

                        // tooltips above
                        foreach(var m in Main.Objects.Markers)
                        {
                           if(m.IsVisible && m.ToolTip != null && m.IsVisible)
                           {
                              if(!string.IsNullOrEmpty(m.ToolTipText))
                              {
                                 var pr = m.Position;
                                 var px = info.Type.Projection.FromLatLngToPixel(pr.Lat, pr.Lng, info.Zoom);

                                 px.Offset(padding, padding);
                                 px.Offset(-topLeftPx.X, -topLeftPx.Y);
                                 px.Offset(m.Offset.X, m.Offset.Y);

                                 gfx.ResetTransform();
                                 gfx.TranslateTransform(-m.LocalPosition.X, -m.LocalPosition.Y);
                                 gfx.TranslateTransform((int)px.X, (int)px.Y);

                                 m.ToolTip.OnRender(gfx);
                              }
                           }
                        }
                        gfx.ResetTransform();
                     }

                     // draw info
                     if(!info.MakeWorldFile)
                     {
                        var rect = new Rectangle();
                        {
                           rect.Location = new Point(padding, padding);
                           rect.Size = new Size((int)pxDelta.X, (int)pxDelta.Y);
                        }

                        using(var f = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold))
                        {
                           // draw bounds & coordinates
                           using(var p = new Pen(Brushes.DimGray, 3))
                           {
                              p.DashStyle = DashStyle.DashDot;

                              gfx.DrawRectangle(p, rect);

                              string topleft = info.Area.LocationTopLeft.ToString();
                              var s = gfx.MeasureString(topleft, f);

                              gfx.DrawString(topleft, f, p.Brush, rect.X + s.Height / 2, rect.Y + s.Height / 2);

                              string rightBottom = new PointLatLng(info.Area.Bottom, info.Area.Right).ToString();
                              var s2 = gfx.MeasureString(rightBottom, f);

                              gfx.DrawString(rightBottom, f, p.Brush, rect.Right - s2.Width - s2.Height / 2, rect.Bottom - s2.Height - s2.Height / 2);
                           }

                           // draw scale
                           using(var p = new Pen(Brushes.Blue, 1))
                           {
                              double rez = info.Type.Projection.GetGroundResolution(info.Zoom, info.Area.Bottom);
                              int px100 = (int)(100.0 / rez); // 100 meters
                              int px1000 = (int)(1000.0 / rez); // 1km   

                              gfx.DrawRectangle(p, rect.X + 10, rect.Bottom - 20, px1000, 10);
                              gfx.DrawRectangle(p, rect.X + 10, rect.Bottom - 20, px100, 10);

                              string leftBottom = "scale: 100m | 1Km";
                              var s = gfx.MeasureString(leftBottom, f);
                              gfx.DrawString(leftBottom, f, p.Brush, rect.X + 10, rect.Bottom - s.Height - 20);
                           }
                        }
                     }
                  }

                  bmpDestination.Save(bigImage, ImageFormat.Jpeg);
               }
            }

            //The worldfile for the original image is:

            //0.000067897543      // the horizontal size of a pixel in coordinate units (longitude degrees in this case);
            //0.0000000
            //0.0000000
            //-0.0000554613012    // the comparable vertical pixel size in latitude degrees, negative because latitude decreases as you go from top to bottom in the image.
            //-111.743323868834   // longitude of the pixel in the upper-left-hand corner.
            //35.1254392635083    // latitude of the pixel in the upper-left-hand corner.

            // generate world file
            if(info.MakeWorldFile)
            {
               string wf = bigImage + "w";
               using(var world = File.CreateText(wf))
               {
                  world.WriteLine("{0:0.000000000000}", (info.Area.WidthLng / pxDelta.X));
                  world.WriteLine("0.0000000");
                  world.WriteLine("0.0000000");
                  world.WriteLine("{0:0.000000000000}", (-info.Area.HeightLat / pxDelta.Y));
                  world.WriteLine("{0:0.000000000000}", info.Area.Left);
                  world.WriteLine("{0:0.000000000000}", info.Area.Top);
                  world.Close();
               }
            }

            if(info.MakeKmz)
            {
               string kmzFile = Path.GetDirectoryName(bigImage) + Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(bigImage) + ".kmz";
               e.Result = kmzFile;

               using(var zip = ZipStorer.Create(kmzFile, "GMap.NET"))
               {
                  zip.AddFile(ZipStorer.Compression.Store, bigImage, "files/map.jpg", "map");

                  using(var readme = new MemoryStream(
                    Encoding.UTF8.GetBytes(
                     string.Format(CultureInfo.InvariantCulture, @"<?xml version=""1.0"" encoding=""UTF-8""?> 
<kml xmlns=""http://www.opengis.net/kml/2.2"" xmlns:gx=""http://www.google.com/kml/ext/2.2"" xmlns:kml=""http://www.opengis.net/kml/2.2"" xmlns:atom=""http://www.w3.org/2005/Atom"">
<GroundOverlay>
	<name>{8}</name>
	<LookAt>
		<longitude>{6}</longitude>
		<latitude>{7}</latitude>
		<altitude>0</altitude>
		<heading>0</heading>
		<tilt>0</tilt>
		<range>69327.55500845652</range>
	</LookAt>
	<color>91ffffff</color>
	<Icon>
		<href>files/map.jpg</href>
	</Icon>
	<gx:LatLonQuad>
		<coordinates>
			{0},{1},0 {2},{3},0 {4},{5},0 {6},{7},0 
		</coordinates>
	</gx:LatLonQuad>
</GroundOverlay>
</kml>", info.Area.Left, info.Area.Bottom,
         info.Area.Right, info.Area.Bottom,
         info.Area.Right, info.Area.Top,
         info.Area.Left, info.Area.Top,
         kmzFile))))
                  {

                     zip.AddStream(ZipStorer.Compression.Store, "doc.kml", readme, DateTime.Now, "kml");
                     zip.Close();
                  }
               }
            }
         }
      }

      readonly List<PointLatLng> _gpxRoute = new List<PointLatLng>();
      RectLatLng _areaGpx = RectLatLng.Empty;

      private void button1_Click(object sender, EventArgs e)
      {
         RectLatLng? area;

         if(checkBoxRoutes.Checked)
         {
            area = Main.MainMap.GetRectOfAllRoutes(null);
            if(!area.HasValue)
            {
               MessageBox.Show("No routes in map", "GMap.NET", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
               return;
            }
         }
         else
         {
            area = Main.MainMap.SelectedArea;
            if(area.Value.IsEmpty)
            {
               MessageBox.Show("Select map area holding ALT", "GMap.NET", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
               return;
            }
         }

         if(!bg.IsBusy)
         {
            lock(_tileArea)
            {
               _tileArea.Clear();
               _tileArea.AddRange(Main.MainMap.MapProvider.Projection.GetAreaTileList(area.Value, (int)numericUpDown1.Value, 1));
               _tileArea.TrimExcess();
            }

            numericUpDown1.Enabled = false;
            progressBar1.Value = 0;
            button1.Enabled = false;
            Main.MainMap.HoldInvalidation = true;

            bg.RunWorkerAsync(new MapInfo(area.Value, (int)numericUpDown1.Value, Main.MainMap.MapProvider, checkBoxWorldFile.Checked, checkBoxKMZ.Checked));
         }
      }

      private void button2_Click(object sender, EventArgs e)
      {
         if(bg.IsBusy)
         {
            bg.CancelAsync();
         }
      }
   }

   public struct MapInfo
   {
      public RectLatLng Area;
      public int Zoom;
      public GMapProvider Type;
      public bool MakeWorldFile;
      public bool MakeKmz;

      public MapInfo(RectLatLng area, int zoom, GMapProvider type, bool makeWorldFile, bool makeKmz)
      {
         this.Area = area;
         this.Zoom = zoom;
         this.Type = type;
         MakeWorldFile = makeWorldFile;
         this.MakeKmz = makeKmz;
      }
   }
}
