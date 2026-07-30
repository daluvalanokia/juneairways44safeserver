using AirwaysMergeSafeServer.Models;

namespace AirwaysMergeSafeServer.Services;

/// <summary>
/// AirCarSpec carries specification metadata for flying cars / eVTOLs
/// including brand logos and side-view silhouettes as inline SVG data URIs.
/// </summary>
public sealed record AirCarSpec(
    string   Type,
    string   Make,
    string   Model,
    string   Size,
    string   Icon,
    string   BrandLogo,
    string   SideViewLogo,
    string[] Colors,
    float    LengthM,
    float    WidthM,
    float    HeightM,
    float    MaxAltitudeM,
    float    CruiseSpeedMph
);

public interface IAirCarRegistry
{
    IReadOnlyList<AirCarSpec> All { get; }
}

public class AirCarRegistry : IAirCarRegistry
{
    public IReadOnlyList<AirCarSpec> All { get; } = new[]
    {
        new AirCarSpec("evtol","Joby Aviation","S4","small","✈",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI0OCIgaGVpZ2h0PSI0OCIgdmlld0JveD0iMCAwIDQ4IDQ4Ij48ZGVmcz48cmFkaWFsR3JhZGllbnQgaWQ9ImciIGN4PSIzNSUiIGN5PSIzNSUiPjxzdG9wIG9mZnNldD0iMCUiIHN0b3AtY29sb3I9IiM2NEI1RjYiLz48c3RvcCBvZmZzZXQ9IjEwMCUiIHN0b3AtY29sb3I9IiMwRDQ3QTEiLz48L3JhZGlhbEdyYWRpZW50PjwvZGVmcz48Y2lyY2xlIGN4PSIyNCIgY3k9IjI0IiByPSIyMiIgZmlsbD0idXJsKCNnKSIgc3Ryb2tlPSIjZmZmIiBzdHJva2Utd2lkdGg9IjEuNSIvPjx0ZXh0IHg9IjI0IiB5PSIzMCIgdGV4dC1hbmNob3I9Im1pZGRsZSIgZmlsbD0iI2ZmZiIgZm9udC1mYW1pbHk9IkFyaWFsIiBmb250LXdlaWdodD0iYm9sZCIgZm9udC1zaXplPSIxNCI+SkE8L3RleHQ+PC9zdmc+",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI4MCIgaGVpZ2h0PSI0MCIgdmlld0JveD0iMCAwIDgwIDQwIj48cGF0aCBkPSJNMTAsMjAgTDMwLDE1IEw2MCwxNSBMNzAsMjAgTDYwLDI1IEwzMCwyNSBaIiBmaWxsPSIjMTU2NUMwIi8+PHRleHQgeD0iNDAiIHk9IjM1IiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmaWxsPSIjZmZmIiBmb250LWZhbWlseT0iQXJpYWwiIGZvbnQtc2l6ZT0iMTAiPkpBPC90ZXh0Pjwvc3ZnPg==",
            new[]{"#1565C0", "#FFFFFF", "#90CAF9", "#37474F"},7.3f,10.7f,3.1f,3000.0f,200.0f),
        new AirCarSpec("evtol","Lilium","Jet","medium","✈",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI0OCIgaGVpZ2h0PSI0OCIgdmlld0JveD0iMCAwIDQ4IDQ4Ij48ZGVmcz48cmFkaWFsR3JhZGllbnQgaWQ9ImciIGN4PSIzNSUiIGN5PSIzNSUiPjxzdG9wIG9mZnNldD0iMCUiIHN0b3AtY29sb3I9IiM0REQwRTEiLz48c3RvcCBvZmZzZXQ9IjEwMCUiIHN0b3AtY29sb3I9IiMwMDYwNjQiLz48L3JhZGlhbEdyYWRpZW50PjwvZGVmcz48Y2lyY2xlIGN4PSIyNCIgY3k9IjI0IiByPSIyMiIgZmlsbD0idXJsKCNnKSIgc3Ryb2tlPSIjZmZmIiBzdHJva2Utd2lkdGg9IjEuNSIvPjx0ZXh0IHg9IjI0IiB5PSIzMCIgdGV4dC1hbmNob3I9Im1pZGRsZSIgZmlsbD0iI2ZmZiIgZm9udC1mYW1pbHk9IkFyaWFsIiBmb250LXdlaWdodD0iYm9sZCIgZm9udC1zaXplPSIxNCI+TEo8L3RleHQ+PC9zdmc+",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI4MCIgaGVpZ2h0PSI0MCIgdmlld0JveD0iMCAwIDgwIDQwIj48cGF0aCBkPSJNMTAsMjAgTDMwLDE1IEw2MCwxNSBMNzAsMjAgTDYwLDI1IEwzMCwyNSBaIiBmaWxsPSIjMDA4MzhGIi8+PHRleHQgeD0iNDAiIHk9IjM1IiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmaWxsPSIjZmZmIiBmb250LWZhbWlseT0iQXJpYWwiIGZvbnQtc2l6ZT0iMTAiPkxKPC90ZXh0Pjwvc3ZnPg==",
            new[]{"#00838F", "#FFFFFF", "#80DEEA", "#212121"},8.5f,13.9f,2.7f,3000.0f,175.0f),
        new AirCarSpec("evtol","Vertical Aerospace","VX4","medium","✈",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI0OCIgaGVpZ2h0PSI0OCIgdmlld0JveD0iMCAwIDQ4IDQ4Ij48ZGVmcz48cmFkaWFsR3JhZGllbnQgaWQ9ImciIGN4PSIzNSUiIGN5PSIzNSUiPjxzdG9wIG9mZnNldD0iMCUiIHN0b3AtY29sb3I9IiM1QzZCQzAiLz48c3RvcCBvZmZzZXQ9IjEwMCUiIHN0b3AtY29sb3I9IiMxQTIzN0UiLz48L3JhZGlhbEdyYWRpZW50PjwvZGVmcz48Y2lyY2xlIGN4PSIyNCIgY3k9IjI0IiByPSIyMiIgZmlsbD0idXJsKCNnKSIgc3Ryb2tlPSIjZmZmIiBzdHJva2Utd2lkdGg9IjEuNSIvPjx0ZXh0IHg9IjI0IiB5PSIzMCIgdGV4dC1hbmNob3I9Im1pZGRsZSIgZmlsbD0iI2ZmZiIgZm9udC1mYW1pbHk9IkFyaWFsIiBmb250LXdlaWdodD0iYm9sZCIgZm9udC1zaXplPSIxNCI+VkE8L3RleHQ+PC9zdmc+",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI4MCIgaGVpZ2h0PSI0MCIgdmlld0JveD0iMCAwIDgwIDQwIj48cGF0aCBkPSJNMTAsMjAgTDMwLDE1IEw2MCwxNSBMNzAsMjAgTDYwLDI1IEwzMCwyNSBaIiBmaWxsPSIjMjgzNTkzIi8+PHRleHQgeD0iNDAiIHk9IjM1IiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmaWxsPSIjZmZmIiBmb250LWZhbWlseT0iQXJpYWwiIGZvbnQtc2l6ZT0iMTAiPlZBPC90ZXh0Pjwvc3ZnPg==",
            new[]{"#283593", "#FFFFFF", "#9FA8DA", "#424242"},13.0f,15.0f,4.0f,3000.0f,200.0f),
        new AirCarSpec("evtol","Archer","Maker","small","✈",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI0OCIgaGVpZ2h0PSI0OCIgdmlld0JveD0iMCAwIDQ4IDQ4Ij48ZGVmcz48cmFkaWFsR3JhZGllbnQgaWQ9ImciIGN4PSIzNSUiIGN5PSIzNSUiPjxzdG9wIG9mZnNldD0iMCUiIHN0b3AtY29sb3I9IiNGRkNBMjgiLz48c3RvcCBvZmZzZXQ9IjEwMCUiIHN0b3AtY29sb3I9IiNFNjUxMDAiLz48L3JhZGlhbEdyYWRpZW50PjwvZGVmcz48Y2lyY2xlIGN4PSIyNCIgY3k9IjI0IiByPSIyMiIgZmlsbD0idXJsKCNnKSIgc3Ryb2tlPSIjZmZmIiBzdHJva2Utd2lkdGg9IjEuNSIvPjx0ZXh0IHg9IjI0IiB5PSIzMCIgdGV4dC1hbmNob3I9Im1pZGRsZSIgZmlsbD0iI2ZmZiIgZm9udC1mYW1pbHk9IkFyaWFsIiBmb250LXdlaWdodD0iYm9sZCIgZm9udC1zaXplPSIxNCI+QU08L3RleHQ+PC9zdmc+",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI4MCIgaGVpZ2h0PSI0MCIgdmlld0JveD0iMCAwIDgwIDQwIj48cGF0aCBkPSJNMTAsMjAgTDMwLDE1IEw2MCwxNSBMNzAsMjAgTDYwLDI1IEwzMCwyNSBaIiBmaWxsPSIjRjU3QzAwIi8+PHRleHQgeD0iNDAiIHk9IjM1IiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmaWxsPSIjZmZmIiBmb250LWZhbWlseT0iQXJpYWwiIGZvbnQtc2l6ZT0iMTAiPkFNPC90ZXh0Pjwvc3ZnPg==",
            new[]{"#F57C00", "#1A1A1A", "#FFFFFF", "#FFE082"},7.5f,12.2f,2.9f,2500.0f,150.0f),
        new AirCarSpec("evtol","Wisk Aero","Gen 6","small","✈",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI0OCIgaGVpZ2h0PSI0OCIgdmlld0JveD0iMCAwIDQ4IDQ4Ij48ZGVmcz48cmFkaWFsR3JhZGllbnQgaWQ9ImciIGN4PSIzNSUiIGN5PSIzNSUiPjxzdG9wIG9mZnNldD0iMCUiIHN0b3AtY29sb3I9IiNGRjk4MDAiLz48c3RvcCBvZmZzZXQ9IjEwMCUiIHN0b3AtY29sb3I9IiNCNzFDMUMiLz48L3JhZGlhbEdyYWRpZW50PjwvZGVmcz48Y2lyY2xlIGN4PSIyNCIgY3k9IjI0IiByPSIyMiIgZmlsbD0idXJsKCNnKSIgc3Ryb2tlPSIjZmZmIiBzdHJva2Utd2lkdGg9IjEuNSIvPjx0ZXh0IHg9IjI0IiB5PSIzMCIgdGV4dC1hbmNob3I9Im1pZGRsZSIgZmlsbD0iI2ZmZiIgZm9udC1mYW1pbHk9IkFyaWFsIiBmb250LXdlaWdodD0iYm9sZCIgZm9udC1zaXplPSIxNCI+V0E8L3RleHQ+PC9zdmc+",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI4MCIgaGVpZ2h0PSI0MCIgdmlld0JveD0iMCAwIDgwIDQwIj48cGF0aCBkPSJNMTAsMjAgTDMwLDE1IEw2MCwxNSBMNzAsMjAgTDYwLDI1IEwzMCwyNSBaIiBmaWxsPSIjRTY1MTAwIi8+PHRleHQgeD0iNDAiIHk9IjM1IiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmaWxsPSIjZmZmIiBmb250LWZhbWlseT0iQXJpYWwiIGZvbnQtc2l6ZT0iMTAiPldBPC90ZXh0Pjwvc3ZnPg==",
            new[]{"#E65100", "#FFFFFF", "#FFCC80", "#263238"},6.4f,10.0f,2.5f,2500.0f,138.0f),
        new AirCarSpec("evtol","Beta Technologies","Alia","medium","✈",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI0OCIgaGVpZ2h0PSI0OCIgdmlld0JveD0iMCAwIDQ4IDQ4Ij48ZGVmcz48cmFkaWFsR3JhZGllbnQgaWQ9ImciIGN4PSIzNSUiIGN5PSIzNSUiPjxzdG9wIG9mZnNldD0iMCUiIHN0b3AtY29sb3I9IiM2NkJCNkEiLz48c3RvcCBvZmZzZXQ9IjEwMCUiIHN0b3AtY29sb3I9IiMxQjVFMjAiLz48L3JhZGlhbEdyYWRpZW50PjwvZGVmcz48Y2lyY2xlIGN4PSIyNCIgY3k9IjI0IiByPSIyMiIgZmlsbD0idXJsKCNnKSIgc3Ryb2tlPSIjZmZmIiBzdHJva2Utd2lkdGg9IjEuNSIvPjx0ZXh0IHg9IjI0IiB5PSIzMCIgdGV4dC1hbmNob3I9Im1pZGRsZSIgZmlsbD0iI2ZmZiIgZm9udC1mYW1pbHk9IkFyaWFsIiBmb250LXdlaWdodD0iYm9sZCIgZm9udC1zaXplPSIxNCI+QlQ8L3RleHQ+PC9zdmc+",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI4MCIgaGVpZ2h0PSI0MCIgdmlld0JveD0iMCAwIDgwIDQwIj48cGF0aCBkPSJNMTAsMjAgTDMwLDE1IEw2MCwxNSBMNzAsMjAgTDYwLDI1IEwzMCwyNSBaIiBmaWxsPSIjMkU3RDMyIi8+PHRleHQgeD0iNDAiIHk9IjM1IiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmaWxsPSIjZmZmIiBmb250LWZhbWlseT0iQXJpYWwiIGZvbnQtc2l6ZT0iMTAiPkJUPC90ZXh0Pjwvc3ZnPg==",
            new[]{"#2E7D32", "#FFFFFF", "#A5D6A7", "#1C313A"},10.5f,15.2f,3.8f,3000.0f,170.0f),
        new AirCarSpec("evtol","EHang","216","small","🛸",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI0OCIgaGVpZ2h0PSI0OCIgdmlld0JveD0iMCAwIDQ4IDQ4Ij48ZGVmcz48cmFkaWFsR3JhZGllbnQgaWQ9ImciIGN4PSIzNSUiIGN5PSIzNSUiPjxzdG9wIG9mZnNldD0iMCUiIHN0b3AtY29sb3I9IiNFRjUzNTAiLz48c3RvcCBvZmZzZXQ9IjEwMCUiIHN0b3AtY29sb3I9IiNCNzFDMUMiLz48L3JhZGlhbEdyYWRpZW50PjwvZGVmcz48Y2lyY2xlIGN4PSIyNCIgY3k9IjI0IiByPSIyMiIgZmlsbD0idXJsKCNnKSIgc3Ryb2tlPSIjZmZmIiBzdHJva2Utd2lkdGg9IjEuNSIvPjx0ZXh0IHg9IjI0IiB5PSIzMCIgdGV4dC1hbmNob3I9Im1pZGRsZSIgZmlsbD0iI2ZmZiIgZm9udC1mYW1pbHk9IkFyaWFsIiBmb250LXdlaWdodD0iYm9sZCIgZm9udC1zaXplPSIxNCI+RUg8L3RleHQ+PC9zdmc+",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI4MCIgaGVpZ2h0PSI0MCIgdmlld0JveD0iMCAwIDgwIDQwIj48cGF0aCBkPSJNMTAsMjAgTDMwLDE1IEw2MCwxNSBMNzAsMjAgTDYwLDI1IEwzMCwyNSBaIiBmaWxsPSIjQzYyODI4Ii8+PHRleHQgeD0iNDAiIHk9IjM1IiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmaWxsPSIjZmZmIiBmb250LWZhbWlseT0iQXJpYWwiIGZvbnQtc2l6ZT0iMTAiPkVIPC90ZXh0Pjwvc3ZnPg==",
            new[]{"#C62828", "#FFFFFF", "#EF9A9A", "#212121"},5.61f,5.61f,1.77f,3000.0f,81.0f),
        new AirCarSpec("evtol","Volocopter","VoloCity","small","✈",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI0OCIgaGVpZ2h0PSI0OCIgdmlld0JveD0iMCAwIDQ4IDQ4Ij48ZGVmcz48cmFkaWFsR3JhZGllbnQgaWQ9ImciIGN4PSIzNSUiIGN5PSIzNSUiPjxzdG9wIG9mZnNldD0iMCUiIHN0b3AtY29sb3I9IiMyOUI2RjYiLz48c3RvcCBvZmZzZXQ9IjEwMCUiIHN0b3AtY29sb3I9IiMwMTU3OUIiLz48L3JhZGlhbEdyYWRpZW50PjwvZGVmcz48Y2lyY2xlIGN4PSIyNCIgY3k9IjI0IiByPSIyMiIgZmlsbD0idXJsKCNnKSIgc3Ryb2tlPSIjZmZmIiBzdHJva2Utd2lkdGg9IjEuNSIvPjx0ZXh0IHg9IjI0IiB5PSIzMCIgdGV4dC1hbmNob3I9Im1pZGRsZSIgZmlsbD0iI2ZmZiIgZm9udC1mYW1pbHk9IkFyaWFsIiBmb250LXdlaWdodD0iYm9sZCIgZm9udC1zaXplPSIxNCI+VkM8L3RleHQ+PC9zdmc+",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI4MCIgaGVpZ2h0PSI0MCIgdmlld0JveD0iMCAwIDgwIDQwIj48cGF0aCBkPSJNMTAsMjAgTDMwLDE1IEw2MCwxNSBMNzAsMjAgTDYwLDI1IEwzMCwyNSBaIiBmaWxsPSIjMDI4OEQxIi8+PHRleHQgeD0iNDAiIHk9IjM1IiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmaWxsPSIjZmZmIiBmb250LWZhbWlseT0iQXJpYWwiIGZvbnQtc2l6ZT0iMTAiPlZDPC90ZXh0Pjwvc3ZnPg==",
            new[]{"#0288D1", "#FFFFFF", "#81D4FA", "#37474F"},9.3f,11.3f,2.5f,2000.0f,68.0f),
        new AirCarSpec("hybrid","AeroMobil","4.0","medium","🚗",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI0OCIgaGVpZ2h0PSI0OCIgdmlld0JveD0iMCAwIDQ4IDQ4Ij48ZGVmcz48cmFkaWFsR3JhZGllbnQgaWQ9ImciIGN4PSIzNSUiIGN5PSIzNSUiPjxzdG9wIG9mZnNldD0iMCUiIHN0b3AtY29sb3I9IiNFQzQwN0EiLz48c3RvcCBvZmZzZXQ9IjEwMCUiIHN0b3AtY29sb3I9IiM4ODBFNEYiLz48L3JhZGlhbEdyYWRpZW50PjwvZGVmcz48Y2lyY2xlIGN4PSIyNCIgY3k9IjI0IiByPSIyMiIgZmlsbD0idXJsKCNnKSIgc3Ryb2tlPSIjZmZmIiBzdHJva2Utd2lkdGg9IjEuNSIvPjx0ZXh0IHg9IjI0IiB5PSIzMCIgdGV4dC1hbmNob3I9Im1pZGRsZSIgZmlsbD0iI2ZmZiIgZm9udC1mYW1pbHk9IkFyaWFsIiBmb250LXdlaWdodD0iYm9sZCIgZm9udC1zaXplPSIxNCI+QU08L3RleHQ+PC9zdmc+",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI4MCIgaGVpZ2h0PSI0MCIgdmlld0JveD0iMCAwIDgwIDQwIj48cGF0aCBkPSJNMTAsMjAgTDMwLDE1IEw2MCwxNSBMNzAsMjAgTDYwLDI1IEwzMCwyNSBaIiBmaWxsPSIjQUQxNDU3Ii8+PHRleHQgeD0iNDAiIHk9IjM1IiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmaWxsPSIjZmZmIiBmb250LWZhbWlseT0iQXJpYWwiIGZvbnQtc2l6ZT0iMTAiPkFNPC90ZXh0Pjwvc3ZnPg==",
            new[]{"#AD1457", "#FFFFFF", "#F48FB1", "#212121"},5.9f,8.8f,1.5f,3000.0f,160.0f),
        new AirCarSpec("gyrocopter","PAL-V","Liberty","small","🚁",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI0OCIgaGVpZ2h0PSI0OCIgdmlld0JveD0iMCAwIDQ4IDQ4Ij48ZGVmcz48cmFkaWFsR3JhZGllbnQgaWQ9ImciIGN4PSIzNSUiIGN5PSIzNSUiPjxzdG9wIG9mZnNldD0iMCUiIHN0b3AtY29sb3I9IiNGRjcwNDMiLz48c3RvcCBvZmZzZXQ9IjEwMCUiIHN0b3AtY29sb3I9IiNCRjM2MEMiLz48L3JhZGlhbEdyYWRpZW50PjwvZGVmcz48Y2lyY2xlIGN4PSIyNCIgY3k9IjI0IiByPSIyMiIgZmlsbD0idXJsKCNnKSIgc3Ryb2tlPSIjZmZmIiBzdHJva2Utd2lkdGg9IjEuNSIvPjx0ZXh0IHg9IjI0IiB5PSIzMCIgdGV4dC1hbmNob3I9Im1pZGRsZSIgZmlsbD0iI2ZmZiIgZm9udC1mYW1pbHk9IkFyaWFsIiBmb250LXdlaWdodD0iYm9sZCIgZm9udC1zaXplPSIxNCI+UFY8L3RleHQ+PC9zdmc+",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI4MCIgaGVpZ2h0PSI0MCIgdmlld0JveD0iMCAwIDgwIDQwIj48cGF0aCBkPSJNMTAsMjAgTDMwLDE1IEw2MCwxNSBMNzAsMjAgTDYwLDI1IEwzMCwyNSBaIiBmaWxsPSIjRDg0MzE1Ii8+PHRleHQgeD0iNDAiIHk9IjM1IiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmaWxsPSIjZmZmIiBmb250LWZhbWlseT0iQXJpYWwiIGZvbnQtc2l6ZT0iMTAiPlBWPC90ZXh0Pjwvc3ZnPg==",
            new[]{"#D84315", "#212121", "#FFAB91", "#FFFFFF"},4.0f,2.0f,3.2f,3500.0f,112.0f),
        new AirCarSpec("hybrid","Terrafugia","Transition","medium","🚗",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI0OCIgaGVpZ2h0PSI0OCIgdmlld0JveD0iMCAwIDQ4IDQ4Ij48ZGVmcz48cmFkaWFsR3JhZGllbnQgaWQ9ImciIGN4PSIzNSUiIGN5PSIzNSUiPjxzdG9wIG9mZnNldD0iMCUiIHN0b3AtY29sb3I9IiM0MkE1RjUiLz48c3RvcCBvZmZzZXQ9IjEwMCUiIHN0b3AtY29sb3I9IiMwRDQ3QTEiLz48L3JhZGlhbEdyYWRpZW50PjwvZGVmcz48Y2lyY2xlIGN4PSIyNCIgY3k9IjI0IiByPSIyMiIgZmlsbD0idXJsKCNnKSIgc3Ryb2tlPSIjZmZmIiBzdHJva2Utd2lkdGg9IjEuNSIvPjx0ZXh0IHg9IjI0IiB5PSIzMCIgdGV4dC1hbmNob3I9Im1pZGRsZSIgZmlsbD0iI2ZmZiIgZm9udC1mYW1pbHk9IkFyaWFsIiBmb250LXdlaWdodD0iYm9sZCIgZm9udC1zaXplPSIxNCI+VEY8L3RleHQ+PC9zdmc+",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI4MCIgaGVpZ2h0PSI0MCIgdmlld0JveD0iMCAwIDgwIDQwIj48cGF0aCBkPSJNMTAsMjAgTDMwLDE1IEw2MCwxNSBMNzAsMjAgTDYwLDI1IEwzMCwyNSBaIiBmaWxsPSIjMTk3NkQyIi8+PHRleHQgeD0iNDAiIHk9IjM1IiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmaWxsPSIjZmZmIiBmb250LWZhbWlseT0iQXJpYWwiIGZvbnQtc2l6ZT0iMTAiPlRGPC90ZXh0Pjwvc3ZnPg==",
            new[]{"#1976D2", "#FFFFFF", "#90CAF9", "#263238"},6.0f,8.2f,2.0f,3000.0f,100.0f),
        new AirCarSpec("evtol","Kitty Hawk","Heaviside","small","✈",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI0OCIgaGVpZ2h0PSI0OCIgdmlld0JveD0iMCAwIDQ4IDQ4Ij48ZGVmcz48cmFkaWFsR3JhZGllbnQgaWQ9ImciIGN4PSIzNSUiIGN5PSIzNSUiPjxzdG9wIG9mZnNldD0iMCUiIHN0b3AtY29sb3I9IiNGRkE3MjYiLz48c3RvcCBvZmZzZXQ9IjEwMCUiIHN0b3AtY29sb3I9IiNFNjUxMDAiLz48L3JhZGlhbEdyYWRpZW50PjwvZGVmcz48Y2lyY2xlIGN4PSIyNCIgY3k9IjI0IiByPSIyMiIgZmlsbD0idXJsKCNnKSIgc3Ryb2tlPSIjZmZmIiBzdHJva2Utd2lkdGg9IjEuNSIvPjx0ZXh0IHg9IjI0IiB5PSIzMCIgdGV4dC1hbmNob3I9Im1pZGRsZSIgZmlsbD0iI2ZmZiIgZm9udC1mYW1pbHk9IkFyaWFsIiBmb250LXdlaWdodD0iYm9sZCIgZm9udC1zaXplPSIxNCI+S0g8L3RleHQ+PC9zdmc+",
            "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSI4MCIgaGVpZ2h0PSI0MCIgdmlld0JveD0iMCAwIDgwIDQwIj48cGF0aCBkPSJNMTAsMjAgTDMwLDE1IEw2MCwxNSBMNzAsMjAgTDYwLDI1IEwzMCwyNSBaIiBmaWxsPSIjRUY2QzAwIi8+PHRleHQgeD0iNDAiIHk9IjM1IiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmaWxsPSIjZmZmIiBmb250LWZhbWlseT0iQXJpYWwiIGZvbnQtc2l6ZT0iMTAiPktIPC90ZXh0Pjwvc3ZnPg==",
            new[]{"#EF6C00", "#FFFFFF", "#FFE082", "#212121"},4.3f,6.1f,1.5f,2500.0f,180.0f),
    };
}
