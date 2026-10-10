using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace PixelHelper;

/// <summary>
/// Deterministic collision-free layout engine for assistant action buttons.
/// Prevents button overlapping in constrained spaces (corners, taskbar edges, small windows).
/// </summary>
public static class ButtonLayoutEngine
{
    public const double DefaultButtonWidth = 142.0;
    public const double CompactButtonWidth = 132.0;
    public const double ButtonHeight = 42.0;
    public const double CanvasWidth = 500.0;
    public const double CanvasHeight = 400.0;
    public const double RobotSize = 96.0;
    public const double Margin = 8.0;
    public const double MinGap = 6.0;

    /// <summary>
    /// Adaptively select button width based on total count and layout type to maximize breathing room.
    /// </summary>
    public static double DetermineButtonWidth(int count, bool isCorner)
    {
        if (count >= 8) return CompactButtonWidth;
        if (count >= 6 && isCorner) return 136.0;
        return DefaultButtonWidth;
    }

    /// <summary>
    /// Calculates collision-free positions for assistant action buttons.
    /// </summary>
    public static (List<Rect> boxes, double btnWidth, double btnHeight) CalculateLayout(
        int count,
        double robotCanvasX,
        double robotCanvasY,
        double winW = CanvasWidth,
        double winH = CanvasHeight,
        bool nearBottom = false,
        bool nearTop = false,
        bool nearRight = false,
        bool nearLeft = false)
    {
        if (count <= 0)
        {
            return (new List<Rect>(), DefaultButtonWidth, ButtonHeight);
        }

        bool isCorner = (nearBottom && nearRight) || (nearBottom && nearLeft) || (nearTop && nearRight) || (nearTop && nearLeft);
        double btnWidth = DetermineButtonWidth(count, isCorner);
        double btnHeight = ButtonHeight;

        double rcX = robotCanvasX + RobotSize / 2.0;
        double rcY = robotCanvasY + RobotSize / 2.0;

        var rawBoxes = new List<Rect>(count);

        if (nearBottom && nearRight)
        {
            // Bottom-right corner (near tray/taskbar):
            // Arrange buttons in a staggered dual-column grid in the upper-left quadrant.
            rawBoxes = LayoutBottomRightCorner(count, robotCanvasX, robotCanvasY, winW, winH, btnWidth, btnHeight);
        }
        else if (nearBottom && nearLeft)
        {
            // Bottom-left corner:
            // Arrange buttons in a staggered dual-column grid in the upper-right quadrant.
            rawBoxes = LayoutBottomLeftCorner(count, robotCanvasX, robotCanvasY, winW, winH, btnWidth, btnHeight);
        }
        else if (nearTop && nearRight)
        {
            // Top-right corner:
            // Arrange buttons in lower-left quadrant.
            rawBoxes = LayoutTopRightCorner(count, robotCanvasX, robotCanvasY, winW, winH, btnWidth, btnHeight);
        }
        else if (nearTop && nearLeft)
        {
            // Top-left corner:
            // Arrange buttons in lower-right quadrant.
            rawBoxes = LayoutTopLeftCorner(count, robotCanvasX, robotCanvasY, winW, winH, btnWidth, btnHeight);
        }
        else if (nearBottom)
        {
            // Along taskbar at bottom: symmetric tiered arch above the robot.
            rawBoxes = LayoutBottomEdgeTiered(count, rcX, robotCanvasY, winW, winH, btnWidth, btnHeight);
        }
        else if (nearTop)
        {
            // Along top edge: symmetric tiered arch below the robot.
            rawBoxes = LayoutTopEdgeTiered(count, rcX, robotCanvasY, winW, winH, btnWidth, btnHeight);
        }
        else if (nearRight)
        {
            // Near right edge: dual-column cascade to the left of the robot.
            rawBoxes = LayoutRightEdge(count, robotCanvasX, robotCanvasY, winW, winH, btnWidth, btnHeight);
        }
        else if (nearLeft)
        {
            // Near left edge: dual-column cascade to the right of the robot.
            rawBoxes = LayoutLeftEdge(count, robotCanvasX, robotCanvasY, winW, winH, btnWidth, btnHeight);
        }
        else
        {
            // Center of screen: full ellipse around the robot.
            rawBoxes = LayoutCenterEllipse(count, rcX, rcY, winW, winH, btnWidth, btnHeight);
        }

        // Apply protective collision solver
        var bounds = new Rect(Margin, Margin, Math.Max(0, winW - 2 * Margin), Math.Max(0, winH - 2 * Margin));
        var obstacle = new Rect(robotCanvasX - 2, robotCanvasY - 2, RobotSize + 4, RobotSize + 4);

        var resolved = ResolveCollisions(rawBoxes, bounds, obstacle, MinGap);
        return (resolved, btnWidth, btnHeight);
    }

    private static List<Rect> LayoutBottomRightCorner(
        int count, double rx, double ry, double winW, double winH, double btnW, double btnH)
    {
        var result = new List<Rect>(count);
        const double gapX = 12.0;
        const double gapY = 8.0;
        double yStep = btnH + gapY;

        if (count <= 3)
        {
            // Single column directly left of the robot
            double colX = Math.Clamp(rx - btnW - gapX, Margin, winW - btnW - Margin);
            double baseY = Math.Min(winH - btnH - Margin, ry + 40);
            for (int i = 0; i < count; i++)
            {
                double y = baseY - (count - 1 - i) * yStep;
                result.Add(new Rect(colX, y, btnW, btnH));
            }
            return result;
        }

        // Dual column staggered fan
        int col1Count = (count + 1) / 2; // Inner column (closest to robot)
        int col2Count = count - col1Count; // Outer column (further left)

        double col1X = Math.Clamp(rx - btnW - gapX, Margin + btnW + gapX, winW - btnW - Margin);
        double col2X = Math.Clamp(col1X - btnW - gapX, Margin, winW - btnW - Margin);

        double baseY1 = Math.Min(winH - btnH - Margin, ry + 40);
        double baseY2 = baseY1 - 25.0; // Staggered by half-step

        // Inner column items
        for (int i = 0; i < col1Count; i++)
        {
            double y = baseY1 - (col1Count - 1 - i) * yStep;
            result.Add(new Rect(col1X, y, btnW, btnH));
        }

        // Outer column items
        for (int i = 0; i < col2Count; i++)
        {
            double y = baseY2 - (col2Count - 1 - i) * yStep;
            result.Add(new Rect(col2X, y, btnW, btnH));
        }

        return result;
    }

    private static List<Rect> LayoutBottomLeftCorner(
        int count, double rx, double ry, double winW, double winH, double btnW, double btnH)
    {
        var result = new List<Rect>(count);
        const double gapX = 12.0;
        const double gapY = 8.0;
        double yStep = btnH + gapY;

        if (count <= 3)
        {
            // Single column directly right of the robot
            double colX = Math.Clamp(rx + RobotSize + gapX, Margin, winW - btnW - Margin);
            double baseY = Math.Min(winH - btnH - Margin, ry + 40);
            for (int i = 0; i < count; i++)
            {
                double y = baseY - (count - 1 - i) * yStep;
                result.Add(new Rect(colX, y, btnW, btnH));
            }
            return result;
        }

        // Dual column staggered fan
        int col1Count = (count + 1) / 2;
        int col2Count = count - col1Count;

        double col1X = Math.Clamp(rx + RobotSize + gapX, Margin, winW - 2 * btnW - 2 * gapX);
        double col2X = Math.Clamp(col1X + btnW + gapX, Margin, winW - btnW - Margin);

        double baseY1 = Math.Min(winH - btnH - Margin, ry + 40);
        double baseY2 = baseY1 - 25.0;

        for (int i = 0; i < col1Count; i++)
        {
            double y = baseY1 - (col1Count - 1 - i) * yStep;
            result.Add(new Rect(col1X, y, btnW, btnH));
        }

        for (int i = 0; i < col2Count; i++)
        {
            double y = baseY2 - (col2Count - 1 - i) * yStep;
            result.Add(new Rect(col2X, y, btnW, btnH));
        }

        return result;
    }

    private static List<Rect> LayoutTopRightCorner(
        int count, double rx, double ry, double winW, double winH, double btnW, double btnH)
    {
        var result = new List<Rect>(count);
        const double gapX = 12.0;
        const double gapY = 8.0;
        double yStep = btnH + gapY;

        int col1Count = count <= 3 ? count : (count + 1) / 2;
        int col2Count = count <= 3 ? 0 : count - col1Count;

        double col1X = Math.Clamp(rx - btnW - gapX, Margin + (col2Count > 0 ? btnW + gapX : 0), winW - btnW - Margin);
        double col2X = Math.Clamp(col1X - btnW - gapX, Margin, winW - btnW - Margin);

        double baseY1 = Math.Max(Margin, ry + RobotSize - 40);
        double baseY2 = baseY1 + 25.0;

        for (int i = 0; i < col1Count; i++)
        {
            double y = baseY1 + i * yStep;
            result.Add(new Rect(col1X, y, btnW, btnH));
        }

        for (int i = 0; i < col2Count; i++)
        {
            double y = baseY2 + i * yStep;
            result.Add(new Rect(col2X, y, btnW, btnH));
        }

        return result;
    }

    private static List<Rect> LayoutTopLeftCorner(
        int count, double rx, double ry, double winW, double winH, double btnW, double btnH)
    {
        var result = new List<Rect>(count);
        const double gapX = 12.0;
        const double gapY = 8.0;
        double yStep = btnH + gapY;

        int col1Count = count <= 3 ? count : (count + 1) / 2;
        int col2Count = count <= 3 ? 0 : count - col1Count;

        double col1X = Math.Clamp(rx + RobotSize + gapX, Margin, winW - btnW - Margin);
        double col2X = Math.Clamp(col1X + btnW + gapX, Margin, winW - btnW - Margin);

        double baseY1 = Math.Max(Margin, ry + RobotSize - 40);
        double baseY2 = baseY1 + 25.0;

        for (int i = 0; i < col1Count; i++)
        {
            double y = baseY1 + i * yStep;
            result.Add(new Rect(col1X, y, btnW, btnH));
        }

        for (int i = 0; i < col2Count; i++)
        {
            double y = baseY2 + i * yStep;
            result.Add(new Rect(col2X, y, btnW, btnH));
        }

        return result;
    }

    private static List<Rect> LayoutBottomEdgeTiered(
        int count, double rcX, double ry, double winW, double winH, double btnW, double btnH)
    {
        var result = new List<Rect>(count);
        const double gapY = 8.0;
        double yStep = btnH + gapY;

        // Base Y for the lowest tier flanking the robot
        double baseY = Math.Min(winH - btnH - Margin, ry - 20.0);

        if (count == 1)
        {
            double x = rcX - btnW / 2.0;
            double y = baseY - yStep;
            result.Add(new Rect(x, y, btnW, btnH));
            return result;
        }

        bool hasCrown = (count % 2 == 1);
        int pairCount = count / 2;
        int totalTiers = pairCount + (hasCrown ? 1 : 0);

        double[] tierDx = new double[pairCount];
        for (int p = 0; p < pairCount; p++)
        {
            if (pairCount == 1)
            {
                tierDx[0] = 130.0;
            }
            else
            {
                double t = (double)p / (pairCount - 1);
                double maxDx = 132.0;
                double minDx = count >= 8 ? 74.0 : 76.0;
                tierDx[p] = maxDx - t * (maxDx - minDx);
            }
        }

        // Top-to-bottom, left-to-right order:
        // If hasCrown, Crown is at the very top (tier totalTiers - 1)
        if (hasCrown)
        {
            double crownY = baseY - (totalTiers - 1) * yStep;
            double crownX = Math.Clamp(rcX - btnW / 2.0, Margin, winW - btnW - Margin);
            result.Add(new Rect(crownX, crownY, btnW, btnH));
        }

        for (int p = pairCount - 1; p >= 0; p--)
        {
            double y = baseY - p * yStep;
            double dx = tierDx[p];

            double leftCenter = rcX - dx;
            double rightCenter = rcX + dx;

            double leftX = Math.Clamp(leftCenter - btnW / 2.0, Margin, winW - btnW - Margin);
            double rightX = Math.Clamp(rightCenter - btnW / 2.0, Margin, winW - btnW - Margin);

            result.Add(new Rect(leftX, y, btnW, btnH));
            result.Add(new Rect(rightX, y, btnW, btnH));
        }

        return result;
    }

    private static List<Rect> LayoutTopEdgeTiered(
        int count, double rcX, double ry, double winW, double winH, double btnW, double btnH)
    {
        var result = new List<Rect>(count);
        const double gapY = 8.0;
        double yStep = btnH + gapY;

        // Base Y: Tier 0 button bottom aligns with the robot bottom (ry + RobotSize)
        double baseY = Math.Max(Margin, ry + RobotSize - btnH);

        if (count == 1)
        {
            double x = rcX - btnW / 2.0;
            double y = ry + RobotSize + 12.0;
            result.Add(new Rect(x, y, btnW, btnH));
            return result;
        }

        bool hasCrown = (count % 2 == 1);
        int pairCount = count / 2;
        int totalTiers = pairCount + (hasCrown ? 1 : 0);

        double[] tierDx = new double[pairCount];
        for (int p = 0; p < pairCount; p++)
        {
            if (pairCount == 1)
            {
                tierDx[0] = 130.0;
            }
            else
            {
                double t = (double)p / (pairCount - 1);
                double maxDx = 132.0;
                double minDx = count >= 8 ? 74.0 : 76.0;
                tierDx[p] = maxDx - t * (maxDx - minDx);
            }
        }

        for (int p = 0; p < pairCount; p++)
        {
            double y = baseY + p * yStep;
            double dx = tierDx[p];

            double leftCenter = rcX - dx;
            double rightCenter = rcX + dx;

            double leftX = Math.Clamp(leftCenter - btnW / 2.0, Margin, winW - btnW - Margin);
            double rightX = Math.Clamp(rightCenter - btnW / 2.0, Margin, winW - btnW - Margin);

            result.Add(new Rect(leftX, y, btnW, btnH));
            result.Add(new Rect(rightX, y, btnW, btnH));
        }

        if (hasCrown)
        {
            double crownY = baseY + pairCount * yStep;
            double crownX = Math.Clamp(rcX - btnW / 2.0, Margin, winW - btnW - Margin);
            result.Add(new Rect(crownX, crownY, btnW, btnH));
        }

        return result;
    }

    private static List<Rect> LayoutRightEdge(
        int count, double rx, double ry, double winW, double winH, double btnW, double btnH)
    {
        var result = new List<Rect>(count);
        const double gapX = 12.0;
        const double gapY = 8.0;
        double yStep = btnH + gapY;

        if (count <= 3)
        {
            double colX = Math.Clamp(rx - btnW - gapX, Margin, winW - btnW - Margin);
            double totalH = count * btnH + (count - 1) * gapY;
            double startY = Math.Clamp(ry + RobotSize / 2.0 - totalH / 2.0, Margin, winH - totalH - Margin);
            for (int i = 0; i < count; i++)
            {
                result.Add(new Rect(colX, startY + i * yStep, btnW, btnH));
            }
            return result;
        }

        int col1Count = (count + 1) / 2;
        int col2Count = count - col1Count;

        double col1X = Math.Clamp(rx - btnW - gapX, Margin + btnW + gapX, winW - btnW - Margin);
        double col2X = Math.Clamp(col1X - btnW - gapX, Margin, winW - btnW - Margin);

        double totalH1 = col1Count * btnH + (col1Count - 1) * gapY;
        double startY1 = Math.Clamp(ry + RobotSize / 2.0 - totalH1 / 2.0, Margin, winH - totalH1 - Margin);

        double totalH2 = col2Count * btnH + (col2Count - 1) * gapY;
        double startY2 = Math.Clamp(ry + RobotSize / 2.0 - totalH2 / 2.0 + 16.0, Margin, winH - totalH2 - Margin);

        for (int i = 0; i < col1Count; i++)
        {
            result.Add(new Rect(col1X, startY1 + i * yStep, btnW, btnH));
        }

        for (int i = 0; i < col2Count; i++)
        {
            result.Add(new Rect(col2X, startY2 + i * yStep, btnW, btnH));
        }

        return result;
    }

    private static List<Rect> LayoutLeftEdge(
        int count, double rx, double ry, double winW, double winH, double btnW, double btnH)
    {
        var result = new List<Rect>(count);
        const double gapX = 12.0;
        const double gapY = 8.0;
        double yStep = btnH + gapY;

        if (count <= 3)
        {
            double colX = Math.Clamp(rx + RobotSize + gapX, Margin, winW - btnW - Margin);
            double totalH = count * btnH + (count - 1) * gapY;
            double startY = Math.Clamp(ry + RobotSize / 2.0 - totalH / 2.0, Margin, winH - totalH - Margin);
            for (int i = 0; i < count; i++)
            {
                result.Add(new Rect(colX, startY + i * yStep, btnW, btnH));
            }
            return result;
        }

        int col1Count = (count + 1) / 2;
        int col2Count = count - col1Count;

        double col1X = Math.Clamp(rx + RobotSize + gapX, Margin, winW - 2 * btnW - 2 * gapX);
        double col2X = Math.Clamp(col1X + btnW + gapX, Margin, winW - btnW - Margin);

        double totalH1 = col1Count * btnH + (col1Count - 1) * gapY;
        double startY1 = Math.Clamp(ry + RobotSize / 2.0 - totalH1 / 2.0, Margin, winH - totalH1 - Margin);

        double totalH2 = col2Count * btnH + (col2Count - 1) * gapY;
        double startY2 = Math.Clamp(ry + RobotSize / 2.0 - totalH2 / 2.0 + 16.0, Margin, winH - totalH2 - Margin);

        for (int i = 0; i < col1Count; i++)
        {
            result.Add(new Rect(col1X, startY1 + i * yStep, btnW, btnH));
        }

        for (int i = 0; i < col2Count; i++)
        {
            result.Add(new Rect(col2X, startY2 + i * yStep, btnW, btnH));
        }

        return result;
    }

    private static List<Rect> LayoutCenterEllipse(
        int count, double rcX, double rcY, double winW, double winH, double btnW, double btnH)
    {
        var result = new List<Rect>(count);

        for (int i = 0; i < count; i++)
        {
            double angle = 2.0 * Math.PI * i / count;
            double rx = count > 6 ? (i % 2 == 1 ? 175.0 : 130.0) : 165.0;
            double ry = count > 6 ? (i % 2 == 1 ? 140.0 : 100.0) : 130.0;

            double deltaX = -rx * Math.Cos(angle);
            double deltaY = -ry * Math.Sin(angle);

            double posX = Math.Clamp(rcX + deltaX - btnW / 2.0, Margin, winW - btnW - Margin);
            double posY = Math.Clamp(rcY + deltaY - btnH / 2.0, Margin, winH - btnH - Margin);
            result.Add(new Rect(posX, posY, btnW, btnH));
        }

        return result;
    }

    /// <summary>
    /// Deterministic iterative AABB collision solver with boundary damping and obstacle avoidance.
    /// </summary>
    public static List<Rect> ResolveCollisions(
        List<Rect> boxes,
        Rect bounds,
        Rect obstacle,
        double minGap = MinGap)
    {
        var result = boxes.Select(b => new Rect(b.X, b.Y, b.Width, b.Height)).ToList();
        if (result.Count <= 1)
        {
            if (result.Count == 1)
            {
                var b = result[0];
                b.X = Math.Clamp(b.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - b.Width));
                b.Y = Math.Clamp(b.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - b.Height));
                result[0] = b;
            }
            return result;
        }

        // Clamp to initial bounds
        for (int i = 0; i < result.Count; i++)
        {
            var b = result[i];
            b.X = Math.Clamp(b.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - b.Width));
            b.Y = Math.Clamp(b.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - b.Height));
            result[i] = b;
        }

        // Iterative relaxation
        for (int iter = 0; iter < 24; iter++)
        {
            bool moved = false;

            // 1. Obstacle avoidance (push boxes away from robot sprite)
            for (int i = 0; i < result.Count; i++)
            {
                var b = result[i];
                if (b.IntersectsWith(obstacle))
                {
                    double ox = obstacle.X + obstacle.Width / 2.0;
                    double oy = obstacle.Y + obstacle.Height / 2.0;
                    double bx = b.X + b.Width / 2.0;
                    double by = b.Y + b.Height / 2.0;

                    double dx = bx - ox;
                    double dy = by - oy;
                    if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001) dy = -1.0;

                    double overlapX = (obstacle.Width + b.Width) / 2.0 + minGap - Math.Abs(dx);
                    double overlapY = (obstacle.Height + b.Height) / 2.0 + minGap - Math.Abs(dy);

                    if (overlapX > 0 && overlapY > 0)
                    {
                        if (overlapX < overlapY)
                        {
                            b.X += (dx >= 0 ? 1.0 : -1.0) * overlapX;
                        }
                        else
                        {
                            b.Y += (dy >= 0 ? 1.0 : -1.0) * overlapY;
                        }
                        b.X = Math.Clamp(b.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - b.Width));
                        b.Y = Math.Clamp(b.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - b.Height));
                        result[i] = b;
                        moved = true;
                    }
                }
            }

            // 2. Pairwise button relaxation
            for (int i = 0; i < result.Count; i++)
            {
                for (int j = i + 1; j < result.Count; j++)
                {
                    var bA = result[i];
                    var bB = result[j];

                    double cAx = bA.X + bA.Width / 2.0;
                    double cAy = bA.Y + bA.Height / 2.0;
                    double cBx = bB.X + bB.Width / 2.0;
                    double cBy = bB.Y + bB.Height / 2.0;

                    double dx = cAx - cBx;
                    double dy = cAy - cBy;
                    if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001) dy = -1.0;

                    double overlapX = (bA.Width + bB.Width) / 2.0 + minGap - Math.Abs(dx);
                    double overlapY = (bA.Height + bB.Height) / 2.0 + minGap - Math.Abs(dy);

                    if (overlapX > 0 && overlapY > 0)
                    {
                        moved = true;

                        if (overlapX < overlapY)
                        {
                            double sign = dx >= 0 ? 1.0 : -1.0;
                            double shift = (overlapX + 0.2) / 2.0;
                            bA.X += sign * shift;
                            bB.X -= sign * shift;
                        }
                        else
                        {
                            double sign = dy >= 0 ? 1.0 : -1.0;
                            double shift = (overlapY + 0.2) / 2.0;
                            bA.Y += sign * shift;
                            bB.Y -= sign * shift;
                        }

                        bA.X = Math.Clamp(bA.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - bA.Width));
                        bA.Y = Math.Clamp(bA.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - bA.Height));
                        bB.X = Math.Clamp(bB.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - bB.Width));
                        bB.Y = Math.Clamp(bB.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - bB.Height));

                        result[i] = bA;
                        result[j] = bB;
                    }
                }
            }

            if (!moved) break;
        }

        // 3. Final safety unclamping pass: ensure no two boxes share the exact same boundary coordinate
        for (int pass = 0; pass < 8; pass++)
        {
            bool anyOverlap = false;
            for (int i = 0; i < result.Count; i++)
            {
                for (int j = i + 1; j < result.Count; j++)
                {
                    var bA = result[i];
                    var bB = result[j];
                    var expandedA = new Rect(bA.X - minGap / 2.0, bA.Y - minGap / 2.0, bA.Width + minGap, bA.Height + minGap);

                    if (expandedA.IntersectsWith(bB))
                    {
                        anyOverlap = true;
                        double dx = (bA.X + bA.Width / 2.0) - (bB.X + bB.Width / 2.0);
                        double dy = (bA.Y + bA.Height / 2.0) - (bB.Y + bB.Height / 2.0);
                        double overlapX = (bA.Width + bB.Width) / 2.0 + minGap - Math.Abs(dx);
                        double overlapY = (bA.Height + bB.Height) / 2.0 + minGap - Math.Abs(dy);

                        if (overlapX < overlapY && Math.Abs(dx) > 1.0)
                        {
                            if (dx >= 0)
                            {
                                bA.X = Math.Min(bounds.Right - bA.Width, bA.X + overlapX / 2.0 + 1.0);
                                bB.X = Math.Max(bounds.Left, bB.X - overlapX / 2.0 - 1.0);
                            }
                            else
                            {
                                bA.X = Math.Max(bounds.Left, bA.X - overlapX / 2.0 - 1.0);
                                bB.X = Math.Min(bounds.Right - bB.Width, bB.X + overlapX / 2.0 + 1.0);
                            }
                        }
                        else
                        {
                            if (dy >= 0)
                            {
                                bA.Y = Math.Min(bounds.Bottom - bA.Height, bA.Y + overlapY / 2.0 + 1.0);
                                bB.Y = Math.Max(bounds.Top, bB.Y - overlapY / 2.0 - 1.0);
                            }
                            else
                            {
                                bA.Y = Math.Max(bounds.Top, bA.Y - overlapY / 2.0 - 1.0);
                                bB.Y = Math.Min(bounds.Bottom - bB.Height, bB.Y + overlapY / 2.0 + 1.0);
                            }
                        }

                        result[i] = bA;
                        result[j] = bB;
                    }
                }
            }
            if (!anyOverlap) break;
        }

        return result;
    }
}
