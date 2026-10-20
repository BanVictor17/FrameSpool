using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace FrameSpool
{
    public partial class PagePreview
    {
        PhotoItem positioningPhoto;
        PhotoPage positioningPage;
        PhotoLayout positioningLayout;
        Point positioningStart;
        PointF positioningOffset;
        int positioningSlot;
        public event Action PhotoPositionChanged;

        void BeginPhotoPositioning(int slot,Point point)
        {
            positioningPhoto=Page.Photos[slot];positioningPage=Page;positioningLayout=LayoutDefinition;
            positioningSlot=slot;positioningStart=point;
            positioningOffset=new PointF(positioningPhoto.OffsetX,positioningPhoto.OffsetY);
            Capture=true;
        }
        bool PositioningCurrent
        {
            get{return positioningPhoto!=null&&Page==positioningPage&&LayoutDefinition==positioningLayout&&
                positioningSlot<Page.Photos.Count&&Page.Photos[positioningSlot]==positioningPhoto&&!EnableManual;}
        }
        void MovePhotoPosition(Point point)
        {
            if(!PositioningCurrent){EndPhotoPositioning();return;}
            var size=SystemInformation.DragSize;
            if(!IsDragging&&new Rectangle(positioningStart.X-size.Width/2,positioningStart.Y-size.Height/2,size.Width,size.Height).Contains(point))return;
            var sheet=Sheet;if(sheet.Width<=0||sheet.Height<=0)return;
            IsDragging=true;Cursor=Cursors.SizeAll;
            var delta=new[]{new PointF(point.X-positioningStart.X,point.Y-positioningStart.Y)};
            var opening=LayoutDefinition.Design?.Elements.Where(FrameDesign.IsOpening).ElementAtOrDefault(positioningSlot);
            // Custom openings rotate the drawing coordinate system as well as the
            // image. Undo that rotation so the photo still follows the pointer.
            if(opening!=null&&opening.Rotation!=0)using(var transform=new Matrix())
            {transform.Rotate(-opening.Rotation);transform.TransformVectors(delta);}
            positioningPhoto.OffsetX=positioningOffset.X+delta[0].X*LayoutDefinition.Width/sheet.Width;
            positioningPhoto.OffsetY=positioningOffset.Y+delta[0].Y*LayoutDefinition.Height/sheet.Height;
            if(PhotoPositionChanged!=null)PhotoPositionChanged();else RefreshScale();
        }
        void EndPhotoPositioning()
        {
            if(positioningPhoto==null)return;
            positioningPhoto=null;positioningPage=null;positioningLayout=null;
            IsDragging=false;Capture=false;Cursor=Cursors.Default;
        }
        internal bool CancelPhotoPositioning()
        {
            if(positioningPhoto==null)return false;
            bool restore=PositioningCurrent;
            if(restore){positioningPhoto.OffsetX=positioningOffset.X;positioningPhoto.OffsetY=positioningOffset.Y;}
            EndPhotoPositioning();
            if(restore){if(PhotoPositionChanged!=null)PhotoPositionChanged();else RefreshScale();}
            return true;
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {base.OnMouseCaptureChanged(e);if(!Capture)EndPhotoPositioning();}
    }
}
