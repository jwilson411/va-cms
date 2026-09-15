namespace VA.CMS.Infrastructure.ContentTypes;

/// <summary>
/// Enumeration of all built-in field types.
/// All 12 types required by FR-SCHEMA-01 are listed here.
/// </summary>
public enum FieldType
{
    ShortText,
    LongText,
    RichText,
    Number,
    Boolean,
    DateTime,
    Url,
    Email,
    MediaReference,
    SingleRelation,
    MultiRelation,
    RepeatableGroup,
    Json,
}
