﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data.Utils;
using Point = Bcfier.Bcf.Bcf2.Point;

namespace Bcfier.Revit.Data
{
  //Methods for working with views
  public static class RevitView
  {
    //<summary>
    //Generate a VisualizationInfo of the current view
    //</summary>
    //<returns></returns>
    public static VisualizationInfo GenerateViewpoint(UIDocument uidoc)
    {
      try
      {
        var doc = uidoc.Document;

        var v = new VisualizationInfo();

        //Corners of the active UI view
        var topLeft = uidoc.GetOpenUIViews()[0].GetZoomCorners()[0];
        var bottomRight = uidoc.GetOpenUIViews()[0].GetZoomCorners()[1];

        //It's a 2D view
        //not supported by BCF, but I store it under a custom 
        //fields using 2D coordinates and sheet id
        if (uidoc.ActiveView.ViewType != ViewType.ThreeD)
        {
          v.SheetCamera = new SheetCamera
          {
            SheetID = uidoc.ActiveView.Id.IntegerValue,
            TopLeft = new Point { X = topLeft.X, Y = topLeft.Y, Z = topLeft.Z },
            BottomRight = new Point { X = bottomRight.X, Y = bottomRight.Y, Z = bottomRight.Z }
          };
        }
        //It's a 3d view
        else
        {
          var viewCenter = new XYZ();
          var view3D = (View3D)uidoc.ActiveView;
          double zoomValue = 1;
          // it is a orthogonal view
          if (!view3D.IsPerspective)
          {
            double x = (topLeft.X + bottomRight.X) / 2;
            double y = (topLeft.Y + bottomRight.Y) / 2;
            double z = (topLeft.Z + bottomRight.Z) / 2;
            //center of the UI view
            viewCenter = new XYZ(x, y, z);

            //vector going from BR to TL
            XYZ diagVector = topLeft.Subtract(bottomRight);
            //length of the vector
            double dist = topLeft.DistanceTo(bottomRight) / 2;

            //ViewToWorldScale value
            zoomValue = dist * Math.Sin(diagVector.AngleTo(view3D.RightDirection)).ToMeters();

            // **** CUSTOM VALUE FOR TEKLA **** //
            // calculated experimentally, not sure why but it works
            //if (UserSettings.Get("optTekla") == "1")
            //  zoomValue = zoomValue * 2.5;
            // **** CUSTOM VALUE FOR TEKLA **** //

            ViewOrientation3D t = RevitUtils.ConvertBasePoint(doc, viewCenter, uidoc.ActiveView.ViewDirection,
            uidoc.ActiveView.UpDirection, false);

            XYZ c = t.EyePosition;
            XYZ vi = t.ForwardDirection;
            XYZ up = t.UpDirection;


            v.OrthogonalCamera = new OrthogonalCamera
            {
              CameraViewPoint =
              {
                X = c.X.ToMeters(),
                Y = c.Y.ToMeters(),
                Z = c.Z.ToMeters()
              },
              CameraUpVector =
              {
                X = up.X.ToMeters(),
                Y = up.Y.ToMeters(),
                Z = up.Z.ToMeters()
              },
              CameraDirection =
              {
                X = vi.X.ToMeters() * -1,
                Y = vi.Y.ToMeters() * -1,
                Z = vi.Z.ToMeters() * -1
              },
              ViewToWorldScale = zoomValue
            };
          }
          // it is a perspective view
          else
          {
            viewCenter = uidoc.ActiveView.Origin;
            //revit default value
            zoomValue = 45;

            ViewOrientation3D t = RevitUtils.ConvertBasePoint(doc, viewCenter, uidoc.ActiveView.ViewDirection,
             uidoc.ActiveView.UpDirection, false);

            XYZ c = t.EyePosition;
            XYZ vi = t.ForwardDirection;
            XYZ up = t.UpDirection;

            v.PerspectiveCamera = new PerspectiveCamera
            {
              CameraViewPoint =
              {
                X = c.X.ToMeters(),
                Y = c.Y.ToMeters(),
                Z = c.Z.ToMeters()
              },
              CameraUpVector =
              {
                X = up.X.ToMeters(),
                Y = up.Y.ToMeters(),
                Z = up.Z.ToMeters()
              },
              CameraDirection =
              {
                X = vi.X.ToMeters() * -1,
                Y = vi.Y.ToMeters() * -1,
                Z = vi.Z.ToMeters() * -1
              },
              FieldOfView = zoomValue
            };
          }

        }
        //COMPONENTS PART
        string versionName = doc.Application.VersionName;
        v.Components = new List<Component>();

        var visibleElems = new FilteredElementCollector(doc, doc.ActiveView.Id)
          .WhereElementIsNotElementType()
          .WhereElementIsViewIndependent()
        .ToElementIds();
        var hiddenElems = new FilteredElementCollector(doc)
          .WhereElementIsNotElementType()
          .WhereElementIsViewIndependent()
          .Where(x => x.IsHidden(doc.ActiveView)
            || !doc.ActiveView.IsElementVisibleInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate, x.Id)).ToList();//would need to check how much this is affecting performance

        var selectedElems = uidoc.Selection.GetElementIds();


        //include only hidden elements and selected in the BCF
        if (visibleElems.Count() > hiddenElems.Count())
        {
          foreach (var elem in hiddenElems)
          {
            v.Components.Add(new Component
            {
              OriginatingSystem = versionName,
              IfcGuid = IfcGuid.ToIfcGuid(ExportUtils.GetExportId(doc, elem.Id)),
              Visible = false,
              Selected = false,
              AuthoringToolId = elem.Id.IntegerValue.ToString()
            });
          }
          foreach (var elem in selectedElems)
          {
            v.Components.Add(new Component
            {
              OriginatingSystem = versionName,
              IfcGuid = IfcGuid.ToIfcGuid(ExportUtils.GetExportId(doc, elem)),
              Visible = true,
              Selected = true,
              AuthoringToolId = elem.IntegerValue.ToString()
            });
          }
        }
        //include only visible elements
        //all the others are hidden
        else
        {
          foreach (var elem in visibleElems)
          {
            v.Components.Add(new Component
            {
              OriginatingSystem = versionName,
              IfcGuid = IfcGuid.ToIfcGuid(ExportUtils.GetExportId(doc, elem)),
              Visible = true,
              Selected = selectedElems.Contains(elem),
              AuthoringToolId = elem.IntegerValue.ToString()
            });
          }
        }
        return v;

      }
      catch (System.Exception ex1)
      {
        TaskDialog.Show("Error generating viewpoint", "exception: " + ex1);
      }
      return null;
    }

  }
}
