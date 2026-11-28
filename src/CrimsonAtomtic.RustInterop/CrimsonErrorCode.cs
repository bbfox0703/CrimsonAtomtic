namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// The crimson-rs C ABI status codes (<c>c_abi::error</c> in the Rust source) and their names.
/// <see cref="CrimsonSaveException.ErrorCode"/> carries one of these; compare against the
/// constants here, never against a literal.
/// </summary>
public static class CrimsonErrorCode
{
    public const int Ok = 0;
    public const int NullArg = -1;
    public const int InvalidPath = -2;
    public const int Io = -3;
    public const int TooSmall = -4;
    public const int BadMagic = -5;
    public const int UnsupportedVersion = -6;
    public const int PayloadOutOfRange = -7;
    public const int Decompress = -8;
    public const int BodyParse = -9;
    public const int OutOfRange = -10;
    public const int BufferTooSmall = -11;
    public const int NotScalar = -12;
    public const int LengthMismatch = -13;
    public const int WriteFailed = -14;
    public const int NotNavigable = -15;
    public const int NotFound = -16;
    public const int ListVariantUnsupported = -17;
    public const int NotScalarFieldKind = -18;
    public const int MutationInvalid = -19;
    public const int NotInlineBytes = -20;
    public const int BatchInProgress = -21;
    public const int BatchNotOpen = -22;
    public const int NotObjectList = -23;
    public const int TransplantTypeMissing = -24;
    public const int TemplateMismatch = -25;

    /// <summary>A length-changing edit on a list whose header counts more elements than it decoded.</summary>
    public const int ListPartiallyDecoded = -26;

    /// <summary>An inserted or transplanted element is of a different class than the list holds.</summary>
    public const int ElementClassMismatch = -27;

    /// <summary>
    /// A write was refused because the save's HMAC did not verify when it was loaded; see
    /// <see cref="ISaveLoader.SetAllowHmacRewrap"/>.
    /// </summary>
    public const int HmacUnverified = -28;

    public const int Panic = -99;

    /// <summary>The Rust-side name of <paramref name="code"/>, or <c>UNKNOWN(n)</c>.</summary>
    public static string Name(int code) => code switch
    {
        Ok => "OK",
        NullArg => "NULL_ARG",
        InvalidPath => "INVALID_PATH",
        Io => "IO",
        TooSmall => "TOO_SMALL",
        BadMagic => "BAD_MAGIC",
        UnsupportedVersion => "UNSUPPORTED_VERSION",
        PayloadOutOfRange => "PAYLOAD_OUT_OF_RANGE",
        Decompress => "DECOMPRESS",
        BodyParse => "BODY_PARSE",
        OutOfRange => "OUT_OF_RANGE",
        BufferTooSmall => "BUFFER_TOO_SMALL",
        NotScalar => "NOT_SCALAR",
        LengthMismatch => "LENGTH_MISMATCH",
        WriteFailed => "WRITE_FAILED",
        NotNavigable => "NOT_NAVIGABLE",
        NotFound => "NOT_FOUND",
        ListVariantUnsupported => "LIST_VARIANT_UNSUPPORTED",
        NotScalarFieldKind => "NOT_SCALAR_FIELD_KIND",
        MutationInvalid => "MUTATION_INVALID",
        NotInlineBytes => "NOT_INLINE_BYTES",
        BatchInProgress => "BATCH_IN_PROGRESS",
        BatchNotOpen => "BATCH_NOT_OPEN",
        NotObjectList => "NOT_OBJECT_LIST",
        TransplantTypeMissing => "TRANSPLANT_TYPE_MISSING",
        TemplateMismatch => "TEMPLATE_MISMATCH",
        ListPartiallyDecoded => "LIST_PARTIALLY_DECODED",
        ElementClassMismatch => "ELEMENT_CLASS_MISMATCH",
        HmacUnverified => "HMAC_UNVERIFIED",
        Panic => "PANIC",
        _ => $"UNKNOWN({code})",
    };
}
