// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// ------------------------------------------------------------------------------
// Changes to this file must follow the https://aka.ms/api-review process.
// ------------------------------------------------------------------------------

namespace System.Net.Mail
{
    public partial class AlternateView : System.Net.Mail.AttachmentBase
    {
        public AlternateView(System.IO.Stream contentStream) : base (default(string)) { }
        public AlternateView(System.IO.Stream contentStream, System.Net.Mime.ContentType? contentType) : base (default(string)) { }
        public AlternateView(System.IO.Stream contentStream, string? mediaType) : base (default(string)) { }
        public AlternateView(string fileName) : base (default(string)) { }
        public AlternateView(string fileName, System.Net.Mime.ContentType? contentType) : base (default(string)) { }
        public AlternateView(string fileName, string? mediaType) : base (default(string)) { }
        public System.Uri? BaseUri { get { throw null; } set { } }
        public System.Net.Mail.LinkedResourceCollection LinkedResources { get { throw null; } }
        public static System.Net.Mail.AlternateView CreateAlternateViewFromString(string content) { throw null; }
        public static System.Net.Mail.AlternateView CreateAlternateViewFromString(string content, System.Net.Mime.ContentType? contentType) { throw null; }
        public static System.Net.Mail.AlternateView CreateAlternateViewFromString(string content, System.Text.Encoding? contentEncoding, string? mediaType) { throw null; }
        protected override void Dispose(bool disposing) { }
    }
    public sealed partial class AlternateViewCollection : System.Collections.ObjectModel.Collection<System.Net.Mail.AlternateView>, System.IDisposable
    {
        internal AlternateViewCollection() { }
        protected override void ClearItems() { }
        public void Dispose() { }
        protected override void InsertItem(int index, System.Net.Mail.AlternateView item) { }
        protected override void RemoveItem(int index) { }
        protected override void SetItem(int index, System.Net.Mail.AlternateView item) { }
    }
    public partial class Attachment : System.Net.Mail.AttachmentBase
    {
        public Attachment(System.IO.Stream contentStream, System.Net.Mime.ContentType contentType) : base (default(string)) { }
        public Attachment(System.IO.Stream contentStream, string? name) : base (default(string)) { }
        public Attachment(System.IO.Stream contentStream, string? name, string? mediaType) : base (default(string)) { }
        public Attachment(string fileName) : base (default(string)) { }
        public Attachment(string fileName, System.Net.Mime.ContentType contentType) : base (default(string)) { }
        public Attachment(string fileName, string? mediaType) : base (default(string)) { }
        public System.Net.Mime.ContentDisposition? ContentDisposition { get { throw null; } }
        public string? Name { get { throw null; } set { } }
        public System.Text.Encoding? NameEncoding { get { throw null; } set { } }
        public static System.Net.Mail.Attachment CreateAttachmentFromString(string content, System.Net.Mime.ContentType contentType) { throw null; }
        public static System.Net.Mail.Attachment CreateAttachmentFromString(string content, string? name) { throw null; }
        public static System.Net.Mail.Attachment CreateAttachmentFromString(string content, string? name, System.Text.Encoding? contentEncoding, string? mediaType) { throw null; }
    }
    public abstract partial class AttachmentBase : System.IDisposable
    {
        protected AttachmentBase(System.IO.Stream contentStream) { }
        protected AttachmentBase(System.IO.Stream contentStream, System.Net.Mime.ContentType? contentType) { }
        protected AttachmentBase(System.IO.Stream contentStream, string? mediaType) { }
        protected AttachmentBase(string fileName) { }
        protected AttachmentBase(string fileName, System.Net.Mime.ContentType? contentType) { }
        protected AttachmentBase(string fileName, string? mediaType) { }
        [System.Diagnostics.CodeAnalysis.AllowNullAttribute]
        public string ContentId { get { throw null; } set { } }
        public System.IO.Stream ContentStream { get { throw null; } }
        public System.Net.Mime.ContentType ContentType { get { throw null; } set { } }
        public System.Net.Mime.TransferEncoding TransferEncoding { get { throw null; } set { } }
        public void Dispose() { }
        protected virtual void Dispose(bool disposing) { }
    }
    public sealed partial class AttachmentCollection : System.Collections.ObjectModel.Collection<System.Net.Mail.Attachment>, System.IDisposable
    {
        internal AttachmentCollection() { }
        protected override void ClearItems() { }
        public void Dispose() { }
        protected override void InsertItem(int index, System.Net.Mail.Attachment item) { }
        protected override void RemoveItem(int index) { }
        protected override void SetItem(int index, System.Net.Mail.Attachment item) { }
    }
    [System.FlagsAttribute]
    public enum DeliveryNotificationOptions
    {
        None = 0,
        OnSuccess = 1,
        OnFailure = 2,
        Delay = 4,
        Never = 134217728,
    }
    public partial class LinkedResource : System.Net.Mail.AttachmentBase
    {
        public LinkedResource(System.IO.Stream contentStream) : base (default(string)) { }
        public LinkedResource(System.IO.Stream contentStream, System.Net.Mime.ContentType? contentType) : base (default(string)) { }
        public LinkedResource(System.IO.Stream contentStream, string? mediaType) : base (default(string)) { }
        public LinkedResource(string fileName) : base (default(string)) { }
        public LinkedResource(string fileName, System.Net.Mime.ContentType? contentType) : base (default(string)) { }
        public LinkedResource(string fileName, string? mediaType) : base (default(string)) { }
        public System.Uri? ContentLink { get { throw null; } set { } }
        public static System.Net.Mail.LinkedResource CreateLinkedResourceFromString(string content) { throw null; }
        public static System.Net.Mail.LinkedResource CreateLinkedResourceFromString(string content, System.Net.Mime.ContentType? contentType) { throw null; }
        public static System.Net.Mail.LinkedResource CreateLinkedResourceFromString(string content, System.Text.Encoding? contentEncoding, string? mediaType) { throw null; }
    }
    public sealed partial class LinkedResourceCollection : System.Collections.ObjectModel.Collection<System.Net.Mail.LinkedResource>, System.IDisposable
    {
        internal LinkedResourceCollection() { }
        protected override void ClearItems() { }
        public void Dispose() { }
        protected override void InsertItem(int index, System.Net.Mail.LinkedResource item) { }
        protected override void RemoveItem(int index) { }
        protected override void SetItem(int index, System.Net.Mail.LinkedResource item) { }
    }
    public partial class MailAddress
    {
        public MailAddress(string address) { }
        public MailAddress(string address, string? displayName) { }
        public MailAddress(string address, string? displayName, System.Text.Encoding? displayNameEncoding) { }
        public string Address { get { throw null; } }
        public string DisplayName { get { throw null; } }
        public string Host { get { throw null; } }
        public string User { get { throw null; } }
        public override bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] object? value) { throw null; }
        public override int GetHashCode() { throw null; }
        public override string ToString() { throw null; }
        public static bool TryCreate([System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] string? address, [System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] out System.Net.Mail.MailAddress? result) { throw null; }
        public static bool TryCreate([System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] string? address, string? displayName, [System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] out System.Net.Mail.MailAddress? result) { throw null; }
        public static bool TryCreate([System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] string? address, string? displayName, System.Text.Encoding? displayNameEncoding, [System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] out System.Net.Mail.MailAddress? result) { throw null; }
    }
    public partial class MailAddressCollection : System.Collections.ObjectModel.Collection<System.Net.Mail.MailAddress>
    {
        public MailAddressCollection() { }
        public void Add(string addresses) { }
        protected override void InsertItem(int index, System.Net.Mail.MailAddress item) { }
        protected override void SetItem(int index, System.Net.Mail.MailAddress item) { }
        public override string ToString() { throw null; }
    }
    public partial class MailMessage : System.IDisposable
    {
        public MailMessage() { }
        public MailMessage(System.Net.Mail.MailAddress from, System.Net.Mail.MailAddress to) { }
        public MailMessage(string from, string to) { }
        public MailMessage(string from, string to, string? subject, string? body) { }
        public System.Net.Mail.AlternateViewCollection AlternateViews { get { throw null; } }
        public System.Net.Mail.AttachmentCollection Attachments { get { throw null; } }
        public System.Net.Mail.MailAddressCollection Bcc { get { throw null; } }
        [System.Diagnostics.CodeAnalysis.AllowNullAttribute]
        public string Body { get { throw null; } set { } }
        public System.Text.Encoding? BodyEncoding { get { throw null; } set { } }
        public System.Net.Mime.TransferEncoding BodyTransferEncoding { get { throw null; } set { } }
        public System.Net.Mail.MailAddressCollection CC { get { throw null; } }
        public System.Net.Mail.DeliveryNotificationOptions DeliveryNotificationOptions { get { throw null; } set { } }
        [System.Diagnostics.CodeAnalysis.DisallowNullAttribute]
        public System.Net.Mail.MailAddress? From { get { throw null; } set { } }
        public System.Collections.Specialized.NameValueCollection Headers { get { throw null; } }
        public System.Text.Encoding? HeadersEncoding { get { throw null; } set { } }
        public bool IsBodyHtml { get { throw null; } set { } }
        public System.Net.Mail.MailPriority Priority { get { throw null; } set { } }
        [System.ObsoleteAttribute("ReplyTo has been deprecated. Use ReplyToList instead, which can accept multiple addresses.")]
        public System.Net.Mail.MailAddress? ReplyTo { get { throw null; } set { } }
        public System.Net.Mail.MailAddressCollection ReplyToList { get { throw null; } }
        [System.Diagnostics.CodeAnalysis.DisallowNullAttribute]
        public System.Net.Mail.MailAddress? Sender { get { throw null; } set { } }
        [System.Diagnostics.CodeAnalysis.AllowNullAttribute]
        public string Subject { get { throw null; } set { } }
        public System.Text.Encoding? SubjectEncoding { get { throw null; } set { } }
        public System.Net.Mail.MailAddressCollection To { get { throw null; } }
        public void Dispose() { }
        protected virtual void Dispose(bool disposing) { }
    }
    public enum MailPriority
    {
        Normal = 0,
        Low = 1,
        High = 2,
    }
    public delegate void SendCompletedEventHandler(object sender, System.ComponentModel.AsyncCompletedEventArgs e);
    [System.Runtime.Versioning.UnsupportedOSPlatformAttribute("browser")]
    public partial class SmtpClient : System.IDisposable
    {
        public SmtpClient() { }
        public SmtpClient(string? host) { }
        public SmtpClient(string? host, int port) { }
        public System.Security.Cryptography.X509Certificates.X509CertificateCollection ClientCertificates { get { throw null; } }
        public System.Net.ICredentialsByHost? Credentials { get { throw null; } set { } }
        public System.Net.Mail.SmtpDeliveryFormat DeliveryFormat { get { throw null; } set { } }
        public System.Net.Mail.SmtpDeliveryMethod DeliveryMethod { get { throw null; } set { } }
        public bool EnableSsl { get { throw null; } set { } }
        [System.Diagnostics.CodeAnalysis.DisallowNullAttribute]
        public string? Host { get { throw null; } set { } }
        public string? PickupDirectoryLocation { get { throw null; } set { } }
        public int Port { get { throw null; } set { } }
        public System.Net.ServicePoint ServicePoint { get { throw null; } }
        public string? TargetName { get { throw null; } set { } }
        public int Timeout { get { throw null; } set { } }
        public bool UseDefaultCredentials { get { throw null; } set { } }
        public event System.Net.Mail.SendCompletedEventHandler? SendCompleted { add { } remove { } }
        public void Dispose() { }
        protected virtual void Dispose(bool disposing) { }
        protected void OnSendCompleted(System.ComponentModel.AsyncCompletedEventArgs e) { }
        public void Send(System.Net.Mail.MailMessage message) { }
        public void Send(string from, string recipients, string? subject, string? body) { }
        public void SendAsync(System.Net.Mail.MailMessage message, object? userToken) { }
        public void SendAsync(string from, string recipients, string? subject, string? body, object? userToken) { }
        public void SendAsyncCancel() { }
        public System.Threading.Tasks.Task SendMailAsync(System.Net.Mail.MailMessage message) { throw null; }
        public System.Threading.Tasks.Task SendMailAsync(System.Net.Mail.MailMessage message, System.Threading.CancellationToken cancellationToken) { throw null; }
        public System.Threading.Tasks.Task SendMailAsync(string from, string recipients, string? subject, string? body) { throw null; }
        public System.Threading.Tasks.Task SendMailAsync(string from, string recipients, string? subject, string? body, System.Threading.CancellationToken cancellationToken) { throw null; }
    }
    public enum SmtpDeliveryFormat
    {
        SevenBit = 0,
        International = 1,
    }
    public enum SmtpDeliveryMethod
    {
        Network = 0,
        SpecifiedPickupDirectory = 1,
        PickupDirectoryFromIis = 2,
    }
    public partial class SmtpException : System.Exception
    {
        public SmtpException() { }
        public SmtpException(System.Net.Mail.SmtpStatusCode statusCode) { }
        public SmtpException(System.Net.Mail.SmtpStatusCode statusCode, string? message) { }
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)]
        [System.ObsoleteAttribute("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.", DiagnosticId="SYSLIB0051", UrlFormat="https://aka.ms/dotnet-warnings/{0}")]
        protected SmtpException(System.Runtime.Serialization.SerializationInfo serializationInfo, System.Runtime.Serialization.StreamingContext streamingContext) { }
        public SmtpException(string? message) { }
        public SmtpException(string? message, System.Exception? innerException) { }
        public System.Net.Mail.SmtpStatusCode StatusCode { get { throw null; } set { } }
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)]
        [System.ObsoleteAttribute("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.", DiagnosticId="SYSLIB0051", UrlFormat="https://aka.ms/dotnet-warnings/{0}")]
        public override void GetObjectData(System.Runtime.Serialization.SerializationInfo serializationInfo, System.Runtime.Serialization.StreamingContext streamingContext) { }
    }
    public partial class SmtpFailedRecipientException : System.Net.Mail.SmtpException
    {
        public SmtpFailedRecipientException() { }
        public SmtpFailedRecipientException(System.Net.Mail.SmtpStatusCode statusCode, string? failedRecipient) { }
        public SmtpFailedRecipientException(System.Net.Mail.SmtpStatusCode statusCode, string? failedRecipient, string? serverResponse) { }
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)]
        [System.ObsoleteAttribute("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.", DiagnosticId="SYSLIB0051", UrlFormat="https://aka.ms/dotnet-warnings/{0}")]
        protected SmtpFailedRecipientException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) { }
        public SmtpFailedRecipientException(string? message) { }
        public SmtpFailedRecipientException(string? message, System.Exception? innerException) { }
        public SmtpFailedRecipientException(string? message, string? failedRecipient, System.Exception? innerException) { }
        public string? FailedRecipient { get { throw null; } }
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)]
        [System.ObsoleteAttribute("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.", DiagnosticId="SYSLIB0051", UrlFormat="https://aka.ms/dotnet-warnings/{0}")]
        public override void GetObjectData(System.Runtime.Serialization.SerializationInfo serializationInfo, System.Runtime.Serialization.StreamingContext streamingContext) { }
    }
    public partial class SmtpFailedRecipientsException : System.Net.Mail.SmtpFailedRecipientException
    {
        public SmtpFailedRecipientsException() { }
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)]
        [System.ObsoleteAttribute("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.", DiagnosticId="SYSLIB0051", UrlFormat="https://aka.ms/dotnet-warnings/{0}")]
        protected SmtpFailedRecipientsException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) { }
        public SmtpFailedRecipientsException(string? message) { }
        public SmtpFailedRecipientsException(string? message, System.Exception? innerException) { }
        public SmtpFailedRecipientsException(string? message, System.Net.Mail.SmtpFailedRecipientException[] innerExceptions) { }
        public System.Net.Mail.SmtpFailedRecipientException[] InnerExceptions { get { throw null; } }
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)]
        [System.ObsoleteAttribute("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.", DiagnosticId="SYSLIB0051", UrlFormat="https://aka.ms/dotnet-warnings/{0}")]
        public override void GetObjectData(System.Runtime.Serialization.SerializationInfo serializationInfo, System.Runtime.Serialization.StreamingContext streamingContext) { }
    }
    public enum SmtpStatusCode
    {
        GeneralFailure = -1,
        SystemStatus = 211,
        HelpMessage = 214,
        ServiceReady = 220,
        ServiceClosingTransmissionChannel = 221,
        Ok = 250,
        UserNotLocalWillForward = 251,
        CannotVerifyUserWillAttemptDelivery = 252,
        StartMailInput = 354,
        ServiceNotAvailable = 421,
        MailboxBusy = 450,
        LocalErrorInProcessing = 451,
        InsufficientStorage = 452,
        ClientNotPermitted = 454,
        CommandUnrecognized = 500,
        SyntaxError = 501,
        CommandNotImplemented = 502,
        BadCommandSequence = 503,
        CommandParameterNotImplemented = 504,
        MustIssueStartTlsFirst = 530,
        MailboxUnavailable = 550,
        UserNotLocalTryAlternatePath = 551,
        ExceededStorageAllocation = 552,
        MailboxNameNotAllowed = 553,
        TransactionFailed = 554,
    }
}
namespace System.Net.Mime
{
    public partial class ContentDisposition
    {
        public ContentDisposition() { }
        public ContentDisposition(string disposition) { }
        public System.DateTime CreationDate { get { throw null; } set { } }
        public string DispositionType { get { throw null; } set { } }
        public string? FileName { get { throw null; } set { } }
        public bool Inline { get { throw null; } set { } }
        public System.DateTime ModificationDate { get { throw null; } set { } }
        public System.Collections.Specialized.StringDictionary Parameters { get { throw null; } }
        public System.DateTime ReadDate { get { throw null; } set { } }
        public long Size { get { throw null; } set { } }
        public override bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] object? rparam) { throw null; }
        public override int GetHashCode() { throw null; }
        public override string ToString() { throw null; }
    }
    public partial class ContentType
    {
        public ContentType() { }
        public ContentType(string contentType) { }
        public string? Boundary { get { throw null; } set { } }
        public string? CharSet { get { throw null; } set { } }
        public string MediaType { get { throw null; } set { } }
        [System.Diagnostics.CodeAnalysis.AllowNullAttribute]
        public string Name { get { throw null; } set { } }
        public System.Collections.Specialized.StringDictionary Parameters { get { throw null; } }
        public override bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] object? rparam) { throw null; }
        public override int GetHashCode() { throw null; }
        public override string ToString() { throw null; }
    }
    public static partial class DispositionTypeNames
    {
        public const string Attachment = "attachment";
        public const string Inline = "inline";
    }
    public static partial class MediaTypeNames
    {
        public static partial class Application
        {
            public const string FormUrlEncoded = "application/x-www-form-urlencoded";
            public const string GZip = "application/gzip";
            public const string Json = "application/json";
            public const string JsonPatch = "application/json-patch+json";
            public const string JsonSequence = "application/json-seq";
            public const string Manifest = "application/manifest+json";
            public const string Octet = "application/octet-stream";
            public const string Pdf = "application/pdf";
            public const string ProblemJson = "application/problem+json";
            public const string ProblemXml = "application/problem+xml";
            public const string Rtf = "application/rtf";
            public const string Soap = "application/soap+xml";
            public const string Wasm = "application/wasm";
            public const string Xml = "application/xml";
            public const string XmlDtd = "application/xml-dtd";
            public const string XmlPatch = "application/xml-patch+xml";
            public const string Yaml = "application/yaml";
            public const string Zip = "application/zip";
        }
        public static partial class Font
        {
            public const string Collection = "font/collection";
            public const string Otf = "font/otf";
            public const string Sfnt = "font/sfnt";
            public const string Ttf = "font/ttf";
            public const string Woff = "font/woff";
            public const string Woff2 = "font/woff2";
        }
        public static partial class Image
        {
            public const string Avif = "image/avif";
            public const string Bmp = "image/bmp";
            public const string Gif = "image/gif";
            public const string Icon = "image/x-icon";
            public const string Jpeg = "image/jpeg";
            public const string Png = "image/png";
            public const string Svg = "image/svg+xml";
            public const string Tiff = "image/tiff";
            public const string Webp = "image/webp";
        }
        public static partial class Multipart
        {
            public const string ByteRanges = "multipart/byteranges";
            public const string FormData = "multipart/form-data";
            public const string Mixed = "multipart/mixed";
            public const string Related = "multipart/related";
        }
        public static partial class Text
        {
            public const string Css = "text/css";
            public const string Csv = "text/csv";
            public const string EventStream = "text/event-stream";
            public const string Html = "text/html";
            public const string JavaScript = "text/javascript";
            public const string Markdown = "text/markdown";
            public const string Plain = "text/plain";
            public const string RichText = "text/richtext";
            public const string Rtf = "text/rtf";
            public const string Xml = "text/xml";
        }
    }
    public enum TransferEncoding
    {
        Unknown = -1,
        QuotedPrintable = 0,
        Base64 = 1,
        SevenBit = 2,
        EightBit = 3,
    }
}
