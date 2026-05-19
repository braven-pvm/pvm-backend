namespace Pvm.Api.Auth;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Operator = "Operator";
    public const string Viewer = "Viewer";

    public static readonly string[] All = [Admin, Operator, Viewer];
    public static readonly string[] Read = [Admin, Operator, Viewer];
    public static readonly string[] Write = [Admin, Operator];
}
