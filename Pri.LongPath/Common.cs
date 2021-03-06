﻿namespace Pri.LongPath {

    using System;
    using System.ComponentModel;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Security.AccessControl;
    using System.Security.Permissions;
    using System.Security.Principal;
    using System.Text;
    using JetBrains.Annotations;

    public static class Common {

        private const UInt32 ProtectedDiscretionaryAcl = 0x80000000;

        private const UInt32 ProtectedSystemAcl = 0x40000000;

        private const UInt32 UnprotectedDiscretionaryAcl = 0x20000000;

        private const UInt32 UnprotectedSystemAcl = 0x10000000;

        public const Int32 DefaultBufferSize = 16384;

        public const Int32 INVALID = -1;

        public const Int32 SUCCESS = 0;

        [NotNull]
        private static String GetMessageFromErrorCode( Int32 errorCode ) {
            var buffer = new StringBuilder( 512 );

            NativeMethods.FormatMessage( NativeMethods.FORMAT_MESSAGE_IGNORE_INSERTS | NativeMethods.FORMAT_MESSAGE_FROM_SYSTEM | NativeMethods.FORMAT_MESSAGE_ARGUMENT_ARRAY,
                IntPtr.Zero, errorCode, 0, buffer, buffer.Capacity, IntPtr.Zero );

            return buffer.ToString();
        }

        public static Boolean EndsWith( [CanBeNull] [NotNull] this String text, Char value ) => !String.IsNullOrEmpty( text ) && text[ text.Length - 1 ] == value;

        public static Boolean Exists( [NotNull] this String path, out Boolean isDirectory ) {
            path = path.ThrowIfBlank();

            if ( path.TryNormalizeLongPath( out var normalizedPath ) || path.IsPathUnc() ) {
                if ( !String.IsNullOrWhiteSpace( normalizedPath ) ) {
                    var errorCode = TryGetFileAttributes( normalizedPath, out var attributes );

                    if ( errorCode == 0 && ( Int32 )attributes != NativeMethods.INVALID_FILE_ATTRIBUTES ) {
                        isDirectory = attributes.IsDirectory();

                        return true;
                    }
                }
            }

            isDirectory = false;

            return false;
        }

        public static FileAttributes GetAttributes( [NotNull]  this String path ) {
            var normalizedPath = path.NormalizeLongPath();

            var errorCode = normalizedPath.TryGetDirectoryAttributes( out var fileAttributes );

            if ( errorCode != NativeMethods.ERROR_SUCCESS ) {
                throw GetExceptionFromWin32Error( errorCode );
            }

            return fileAttributes;
        }

        public static FileAttributes GetAttributes( [NotNull]  this String path, out Int32 errorCode ) {
            path = path.ThrowIfBlank();

            var normalizedPath = path.NormalizeLongPath();

            errorCode = normalizedPath.TryGetDirectoryAttributes( out var fileAttributes );

            return fileAttributes;
        }

        [NotNull]
        public static Exception GetExceptionFromLastWin32Error() => GetExceptionFromLastWin32Error( "path" );

        [NotNull]
        public static Exception GetExceptionFromLastWin32Error( [NotNull] String parameterName ) => GetExceptionFromWin32Error( Marshal.GetLastWin32Error(), parameterName );

        [NotNull]
        public static Exception GetExceptionFromWin32Error( Int32 errorCode ) => GetExceptionFromWin32Error( errorCode, "path" );

        [NotNull]
        public static Exception GetExceptionFromWin32Error( Int32 errorCode, [NotNull] String parameterName ) {
            var message = GetMessageFromErrorCode( errorCode );

            switch ( errorCode ) {
                case NativeMethods.ERROR_FILE_NOT_FOUND: return new FileNotFoundException( message );

                case NativeMethods.ERROR_PATH_NOT_FOUND: return new DirectoryNotFoundException( message );

                case NativeMethods.ERROR_ACCESS_DENIED: return new UnauthorizedAccessException( message );

                case NativeMethods.ERROR_FILENAME_EXCED_RANGE: return new PathTooLongException( message );

                case NativeMethods.ERROR_INVALID_DRIVE: return new DriveNotFoundException( message );

                case NativeMethods.ERROR_OPERATION_ABORTED: return new OperationCanceledException( message );

                case NativeMethods.ERROR_INVALID_NAME: return new ArgumentException( message, parameterName );

                default: return new IOException( message, NativeMethods.MakeHRFromErrorCode( errorCode ) );
            }
        }

        public static FileAttributes GetFileAttributes( [NotNull]  this String path ) {

            var normalizedPath = path.ThrowIfBlank().NormalizeLongPath();

            var errorCode = TryGetFileAttributes( normalizedPath, out var fileAttributes );

            if ( errorCode != NativeMethods.ERROR_SUCCESS ) {
                throw GetExceptionFromWin32Error( errorCode );
            }

            return fileAttributes;
        }

        public static Boolean IsPathDots( [NotNull]  this String path ) {
            path = path.ThrowIfBlank();

            return path == "." || path == "..";
        }

        public static Boolean IsPathUnc( [NotNull]  this String path ) {
            path = path.ThrowIfBlank();

            if ( path.StartsWith( Path.UNCLongPathPrefix, StringComparison.Ordinal ) ) {
                return true;
            }

            return Uri.TryCreate( path.ThrowIfBlank(), UriKind.Absolute, out var uri ) && uri.IsUnc;
        }

        /// <summary>
        ///     Capture the <see cref="Uri" /> from <paramref name="path" />
        /// </summary>
        /// <param name="path"></param>
        /// <param name="uri"></param>
        /// <returns></returns>
        public static Boolean IsPathUnc( [NotNull]  this String path, [CanBeNull] out Uri uri ) {
            path = path.ThrowIfBlank();

            if ( path.StartsWith( Path.UNCLongPathPrefix, StringComparison.Ordinal ) ) {
                uri = null;

                return true;
            }

            return Uri.TryCreate( path.ThrowIfBlank(), UriKind.Absolute, out uri ) && uri.IsUnc;
        }

        [NotNull]
        public static String NormalizeSearchPattern( [CanBeNull] [NotNull] this String searchPattern ) =>
            String.IsNullOrEmpty( searchPattern ) || searchPattern == "." ? "*" : searchPattern;

        public static void SetAccessControlExtracted( [NotNull] this FileSystemSecurity security, [NotNull] String name ) {
            if ( security == null ) {
                throw new ArgumentNullException( paramName: nameof( security ) );
            }

            name = name.ThrowIfBlank();

            var includeSections = AccessControlSections.Owner | AccessControlSections.Group;

            if ( security.GetAccessRules( true, false, typeof( SecurityIdentifier ) ).Count > 0 ) {
                includeSections |= AccessControlSections.Access;
            }

            if ( security.GetAuditRules( true, false, typeof( SecurityIdentifier ) ).Count > 0 ) {
                includeSections |= AccessControlSections.Audit;
            }

            UInt32 securityInfo = 0;
            SecurityIdentifier owner = null;
            SecurityIdentifier group = null;
            SystemAcl sacl = null;
            DiscretionaryAcl dacl = null;

            if ( ( includeSections & AccessControlSections.Owner ) != AccessControlSections.None ) {
                owner = security.GetOwner( typeof( SecurityIdentifier ) ) as SecurityIdentifier;

                if ( owner != null ) {
                    securityInfo |= ( UInt32 )SecurityInfos.Owner;
                }
            }

            if ( ( includeSections & AccessControlSections.Group ) != AccessControlSections.None ) {
                group = security.GetGroup( typeof( SecurityIdentifier ) ) as SecurityIdentifier;

                if ( group != null ) {
                    securityInfo |= ( UInt32 )SecurityInfos.Group;
                }
            }

            var securityDescriptorBinaryForm = security.GetSecurityDescriptorBinaryForm();

            var rawSecurityDescriptor = new RawSecurityDescriptor( securityDescriptorBinaryForm, 0 );
            var isDiscretionaryAclPresent = ( rawSecurityDescriptor.ControlFlags & ControlFlags.DiscretionaryAclPresent ) != ControlFlags.None;

            if ( ( includeSections & AccessControlSections.Audit ) != AccessControlSections.None ) {
                securityInfo |= ( UInt32 )SecurityInfos.SystemAcl;

                var isSystemAclPresent = ( rawSecurityDescriptor.ControlFlags & ControlFlags.SystemAclPresent ) != ControlFlags.None;

                if ( isSystemAclPresent && rawSecurityDescriptor.SystemAcl != null && rawSecurityDescriptor.SystemAcl.Count > 0 ) {

                    // are all system acls on a file not a container?
                    const Boolean notAContainer = false;
                    const Boolean notADirectoryObjectACL = false;

                    sacl = new SystemAcl( notAContainer, notADirectoryObjectACL, rawSecurityDescriptor.SystemAcl );
                }

                if ( ( rawSecurityDescriptor.ControlFlags & ControlFlags.SystemAclProtected ) == ControlFlags.None ) {
                    securityInfo |= UnprotectedSystemAcl;
                }
                else {
                    securityInfo |= ProtectedSystemAcl;
                }
            }

            if ( ( includeSections & AccessControlSections.Access ) != AccessControlSections.None && isDiscretionaryAclPresent ) {
                securityInfo |= ( UInt32 )SecurityInfos.DiscretionaryAcl;

                dacl = new DiscretionaryAcl( false, false, rawSecurityDescriptor.DiscretionaryAcl );

                securityInfo = ( rawSecurityDescriptor.ControlFlags & ControlFlags.DiscretionaryAclProtected ) == ControlFlags.None ?
                    securityInfo | UnprotectedDiscretionaryAcl :
                    securityInfo | ProtectedDiscretionaryAcl;
            }

            if ( securityInfo == 0 ) {
                return;
            }

            var errorNum = SetSecurityInfo( ResourceType.FileObject, name, ( SecurityInfos )securityInfo, owner, group, sacl, dacl ); //eh?

            if ( errorNum != 0 ) {
                var exception = GetExceptionFromWin32Error( errorNum, name );

                throw exception;
            }
        }

        public static void SetAttributes( [NotNull]  this String path, FileAttributes fileAttributes ) {
            var normalizedPath = path.ThrowIfBlank().NormalizeLongPath();

            if ( !NativeMethods.SetFileAttributes( normalizedPath, fileAttributes ) ) {
                throw GetExceptionFromLastWin32Error();
            }
        }

        public static Int32 SetSecurityInfo( ResourceType type, [NotNull] String name, SecurityInfos securityInformation,
            [CanBeNull] SecurityIdentifier owner, [CanBeNull] SecurityIdentifier group, [CanBeNull] GenericAcl sacl, [CanBeNull] GenericAcl dacl ) {

            name = name.ThrowIfBlank();

            if ( !Enum.IsDefined( enumType: typeof( ResourceType ), type ) ) {
                throw new InvalidEnumArgumentException( argumentName: nameof( type ), invalidValue: ( Int32 )type, enumClass: typeof( ResourceType ) );
            }

            if ( !Enum.IsDefined( enumType: typeof( SecurityInfos ), securityInformation ) ) {
                throw new InvalidEnumArgumentException( argumentName: nameof( securityInformation ), invalidValue: ( Int32 )securityInformation,
                    enumClass: typeof( SecurityInfos ) );
            }

            Int32 errorCode;
            Int32 Length;
            Byte[] OwnerBinary = null, GroupBinary = null, SaclBinary = null, DaclBinary = null;
            Privilege securityPrivilege = null;

            // Demand unmanaged code permission
            // The integrator layer is free to assert this permission and, in turn, demand another permission of its caller

            new SecurityPermission( SecurityPermissionFlag.UnmanagedCode ).Demand();

            if ( owner != null ) {
                Length = owner.BinaryLength;
                OwnerBinary = new Byte[ Length ];
                owner.GetBinaryForm( OwnerBinary, 0 );
            }

            if ( group != null ) {
                Length = group.BinaryLength;
                GroupBinary = new Byte[ Length ];
                group.GetBinaryForm( GroupBinary, 0 );
            }

            if ( dacl != null ) {
                Length = dacl.BinaryLength;
                DaclBinary = new Byte[ Length ];
                dacl.GetBinaryForm( DaclBinary, 0 );
            }

            if ( sacl != null ) {
                Length = sacl.BinaryLength;
                SaclBinary = new Byte[ Length ];
                sacl.GetBinaryForm( SaclBinary, 0 );
            }

            if ( ( securityInformation & SecurityInfos.SystemAcl ) != 0 ) {

                // Enable security privilege if trying to set a SACL.
                // Note: even setting it by handle needs this privilege enabled!

                securityPrivilege = new Privilege( Privilege.Security );
            }

            // Ensure that the finally block will execute
            RuntimeHelpers.PrepareConstrainedRegions();

            try {
                if ( securityPrivilege != null ) {
                    try {
                        securityPrivilege.Enable();
                    }
                    catch ( PrivilegeNotHeldException ) {

                        // we will ignore this exception and press on just in case this is a remote resource
                    }
                }

                errorCode = ( Int32 )NativeMethods.SetSecurityInfoByName( name, ( UInt32 )type, ( UInt32 )securityInformation, OwnerBinary, GroupBinary, DaclBinary,
                    SaclBinary );

                switch ( errorCode ) {
                    case NativeMethods.ERROR_NOT_ALL_ASSIGNED:
                    case NativeMethods.ERROR_PRIVILEGE_NOT_HELD:

                        throw new PrivilegeNotHeldException( Privilege.Security );
                    case NativeMethods.ERROR_ACCESS_DENIED:
                    case NativeMethods.ERROR_CANT_OPEN_ANONYMOUS:

                        throw new UnauthorizedAccessException();
                }

                if ( errorCode != NativeMethods.ERROR_SUCCESS ) {
                    goto Error;
                }
            }
            catch {

                // protection against exception filter-based luring attacks
                securityPrivilege?.Revert();

                throw;
            }
            finally {
                securityPrivilege?.Revert();
            }

            return 0;

            Error:

            if ( errorCode == NativeMethods.ERROR_NOT_ENOUGH_MEMORY ) {
                throw new OutOfMemoryException();
            }

            return errorCode;
        }

        /// <summary>
        /// Returns the trimmed string or throws <see cref="ArgumentNullException"/> if null, whitespace, or empty.
        /// </summary>
        /// <param name="path"></param>
        /// <exception cref="ArgumentNullException">Gets thrown if the <paramref name="path" /> is null, whitespace, or empty.</exception>
        [MethodImpl( MethodImplOptions.AggressiveInlining )]
        [NotNull]
        [Pure]
        public static String ThrowIfBlank( this String path ) {
            if ( String.IsNullOrWhiteSpace( path = path?.Trim() ) ) {
                throw new ArgumentException( message: "Value cannot be null or whitespace.", paramName: nameof( path ) );
            }

            return path;
        }

        public static void ThrowIfError( Int32 errorCode, IntPtr byteArray ) {
            if ( errorCode == NativeMethods.ERROR_SUCCESS ) {
                if ( IntPtr.Zero.Equals( byteArray ) ) {

                    //
                    // This means that the object doesn't have a security descriptor. And thus we throw
                    // a specific exception for the caller to catch and handle properly.
                    //
                    throw new InvalidOperationException( "Object does not have security descriptor," );
                }
            }
            else {
                switch ( errorCode ) {
                    case NativeMethods.ERROR_NOT_ALL_ASSIGNED:
                    case NativeMethods.ERROR_PRIVILEGE_NOT_HELD:

                        throw new PrivilegeNotHeldException( "SeSecurityPrivilege" );
                    case NativeMethods.ERROR_ACCESS_DENIED:
                    case NativeMethods.ERROR_CANT_OPEN_ANONYMOUS:
                    case NativeMethods.ERROR_LOGON_FAILURE:

                        throw new UnauthorizedAccessException();
                    case NativeMethods.ERROR_NOT_ENOUGH_MEMORY: throw new OutOfMemoryException();
                    case NativeMethods.ERROR_BAD_NETPATH:
                    case NativeMethods.ERROR_NETNAME_DELETED:
                    default:

                        throw new IOException( NativeMethods.GetMessage( errorCode ), NativeMethods.MakeHRFromErrorCode( errorCode ) );
                }
            }
        }

        public static void ThrowIOError( Int32 errorCode, [NotNull] String maybeFullPath ) {
            if ( String.IsNullOrWhiteSpace( value: maybeFullPath ) ) {
                throw new ArgumentException( message: "Value cannot be null or whitespace.", paramName: nameof( maybeFullPath ) );
            }

            // This doesn't have to be perfect, but is a perf optimization.
            var isInvalidPath = errorCode == NativeMethods.ERROR_INVALID_NAME || errorCode == NativeMethods.ERROR_BAD_PATHNAME;
            var str = isInvalidPath ? maybeFullPath.GetFileName() : maybeFullPath;

            switch ( errorCode ) {
                case NativeMethods.ERROR_FILE_NOT_FOUND:

                    if ( str.Length == 0 ) {
                        throw new FileNotFoundException( "Empty filename" );
                    }
                    else {
                        throw new FileNotFoundException( $"File {str} not found", str );
                    }

                case NativeMethods.ERROR_PATH_NOT_FOUND:

                    if ( str.Length == 0 ) {
                        throw new DirectoryNotFoundException( "Empty directory" );
                    }
                    else {
                        throw new DirectoryNotFoundException( $"Directory {str} not found" );
                    }

                case NativeMethods.ERROR_ACCESS_DENIED:

                    if ( str.Length == 0 ) {
                        throw new UnauthorizedAccessException( "Empty path" );
                    }
                    else {
                        throw new UnauthorizedAccessException( $"Access denied accessing {str}" );
                    }

                case NativeMethods.ERROR_ALREADY_EXISTS:

                    if ( str.Length == 0 ) {
                        goto default;
                    }

                    throw new IOException( $"File {str}", NativeMethods.MakeHRFromErrorCode( errorCode ) );

                case NativeMethods.ERROR_FILENAME_EXCED_RANGE: throw new PathTooLongException( "Path too long" );

                case NativeMethods.ERROR_INVALID_DRIVE: throw new DriveNotFoundException( $"Drive {str} not found" );

                case NativeMethods.ERROR_INVALID_PARAMETER: throw new IOException( NativeMethods.GetMessage( errorCode ), NativeMethods.MakeHRFromErrorCode( errorCode ) );

                case NativeMethods.ERROR_SHARING_VIOLATION:

                    if ( str.Length == 0 ) {
                        throw new IOException( "Sharing violation with empty filename", NativeMethods.MakeHRFromErrorCode( errorCode ) );
                    }
                    else {
                        throw new IOException( $"Sharing violation: {str}", NativeMethods.MakeHRFromErrorCode( errorCode ) );
                    }

                case NativeMethods.ERROR_FILE_EXISTS:

                    if ( str.Length == 0 ) {
                        goto default;
                    }

                    throw new IOException( $"File exists {str}", NativeMethods.MakeHRFromErrorCode( errorCode ) );

                case NativeMethods.ERROR_OPERATION_ABORTED: throw new OperationCanceledException();

                default: throw new IOException( NativeMethods.GetMessage( errorCode ), NativeMethods.MakeHRFromErrorCode( errorCode ) );
            }
        }

        public static SecurityInfos ToSecurityInfos( this AccessControlSections accessControlSections ) {
            SecurityInfos securityInfos = 0;

            if ( ( accessControlSections & AccessControlSections.Owner ) != 0 ) {
                securityInfos |= SecurityInfos.Owner;
            }

            if ( ( accessControlSections & AccessControlSections.Group ) != 0 ) {
                securityInfos |= SecurityInfos.Group;
            }

            if ( ( accessControlSections & AccessControlSections.Access ) != 0 ) {
                securityInfos |= SecurityInfos.DiscretionaryAcl;
            }

            if ( ( accessControlSections & AccessControlSections.Audit ) != 0 ) {
                securityInfos |= SecurityInfos.SystemAcl;
            }

            return securityInfos;
        }

        public static Int32 TryGetDirectoryAttributes( [NotNull]  this String normalizedPath, out FileAttributes attributes ) => TryGetFileAttributes( normalizedPath.ThrowIfBlank(), out attributes );

        public static Int32 TryGetFileAttributes( [NotNull] String normalizedPath, out FileAttributes attributes ) {

            var data = new WIN32_FILE_ATTRIBUTE_DATA();

            var errorMode = NativeMethods.SetErrorMode( 1 );

            try {

                if ( NativeMethods.GetFileAttributesEx( normalizedPath.ThrowIfBlank(), 0, ref data ) ) {
                    attributes = data.fileAttributes;

                    return SUCCESS;
                }
            }
            finally {
                NativeMethods.SetErrorMode( errorMode );
            }

            attributes = ( FileAttributes )NativeMethods.INVALID_FILE_ATTRIBUTES;

            return Marshal.GetLastWin32Error();
        }
    }
}