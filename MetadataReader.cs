using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.IO;

namespace sym_fetch
{
	public sealed class MetadataReader
	{
		public static Metadata Read(string filePath)
		{
			using (FileStream stream = new FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read))
			{
				BinaryReader reader = new BinaryReader(stream);

				var dosHeader = FromBinaryReader<IMAGE_DOS_HEADER>(reader);

				// Add 4 bytes to the offset
				stream.Seek(dosHeader.e_lfanew, SeekOrigin.Begin);

				UInt32 ntHeadersSignature = reader.ReadUInt32();
				var fileHeader = FromBinaryReader<IMAGE_FILE_HEADER>(reader);

				var is32bit = (fileHeader.Machine == 332);

				uint vaddr = 0;
				uint cbDebug = 0;

				if (is32bit)
				{
					var optionalHeader32 = FromBinaryReader<IMAGE_OPTIONAL_HEADER32>(reader);
					cbDebug = optionalHeader32.Debug.Size;
					vaddr = optionalHeader32.Debug.VirtualAddress;
				}
				else
				{
					var optionalHeader64 = FromBinaryReader<IMAGE_OPTIONAL_HEADER64>(reader);
					cbDebug = optionalHeader64.Debug.Size;
					vaddr = optionalHeader64.Debug.VirtualAddress;
				}

				uint offDebug = 0;
				long cbFromHeader = 0;
				int loopexit = 0;

				var imageSectionHeaders = new IMAGE_SECTION_HEADER[fileHeader.NumberOfSections];

				for (int headerNo = 0; headerNo < imageSectionHeaders.Length; ++headerNo)
				{
					var header = imageSectionHeaders[headerNo] = FromBinaryReader<IMAGE_SECTION_HEADER>(reader);

					if (header.PointerToRawData != 0
						&& header.SizeOfRawData != 0
						&& cbFromHeader < (long)(header.PointerToRawData + header.SizeOfRawData))
					{
						cbFromHeader = (long)(header.PointerToRawData + header.SizeOfRawData);
					}

					if (cbDebug != 0)
					{
						if (header.VirtualAddress <= vaddr
							&& (header.VirtualAddress + header.SizeOfRawData) > vaddr)
						{
							offDebug = vaddr - header.VirtualAddress + header.PointerToRawData;
						}
					}
				}

				stream.Seek(offDebug, SeekOrigin.Begin);

				IMAGE_DEBUG_DIRECTORY_RAW debugInfo = default(IMAGE_DEBUG_DIRECTORY_RAW);
				while (cbDebug >= Marshal.SizeOf(typeof(IMAGE_DEBUG_DIRECTORY)))
				{
					if (loopexit == 0)
					{
						var imageDebugDirectory = FromBinaryReader<IMAGE_DEBUG_DIRECTORY>(reader);
						long seekPosition = stream.Position;

						if (imageDebugDirectory.Type == 0x2)
						{
							stream.Seek(imageDebugDirectory.PointerToRawData, SeekOrigin.Begin);
							debugInfo = FromBinaryReader<IMAGE_DEBUG_DIRECTORY_RAW>(reader);
							loopexit = 1;

							// Downloading logic for .NET native images
							if (new string(debugInfo.name).Contains(".ni."))
							{
								stream.Seek(seekPosition, SeekOrigin.Begin);
								loopexit = 0;
							}
						}

						if (imageDebugDirectory.PointerToRawData != 0
							&& imageDebugDirectory.SizeOfData != 0
							&& (cbFromHeader < (long)(imageDebugDirectory.PointerToRawData + imageDebugDirectory.SizeOfData)))
						{
							cbFromHeader = (long)(imageDebugDirectory.PointerToRawData + imageDebugDirectory.SizeOfData);
						}
					}

					cbDebug -= (uint)Marshal.SizeOf(typeof(IMAGE_DEBUG_DIRECTORY));
				}

				if (loopexit != 0)
				{
					var name = new string(debugInfo.name);
					name = name.Remove(name.IndexOf("\0"));

					return new Metadata()
					{
						PdbName = name,
						PdbAge = debugInfo.age.ToString("X"),
						DebugGUID = debugInfo.guid
					};
				}
			}

			return null;
		}

		public static T FromBinaryReader<T>(BinaryReader reader)
		{
			GCHandle handle = default(GCHandle);
			
			try {
				byte[] bytes = reader.ReadBytes(Marshal.SizeOf(typeof(T)));
				handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
				return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
			}
			finally {
				if (handle.IsAllocated) {
					handle.Free();
				}
			}
		}

		public sealed class Metadata
		{
			public string PdbName { get; set; }
			public string PdbAge { get; set; }
			public Guid DebugGUID { get; set; }
		}

		#region File Header Structures

		public struct IMAGE_DOS_HEADER
		{
			public UInt16 e_magic;              // Magic number
			public UInt16 e_cblp;               // Bytes on last page of file
			public UInt16 e_cp;                 // Pages in file
			public UInt16 e_crlc;               // Relocations
			public UInt16 e_cparhdr;            // Size of header in paragraphs
			public UInt16 e_minalloc;           // Minimum extra paragraphs needed
			public UInt16 e_maxalloc;           // Maximum extra paragraphs needed
			public UInt16 e_ss;                 // Initial (relative) SS value
			public UInt16 e_sp;                 // Initial SP value
			public UInt16 e_csum;               // Checksum
			public UInt16 e_ip;                 // Initial IP value
			public UInt16 e_cs;                 // Initial (relative) CS value
			public UInt16 e_lfarlc;             // File address of relocation table
			public UInt16 e_ovno;               // Overlay number
			public UInt16 e_res_0;              // Reserved words
			public UInt16 e_res_1;              // Reserved words
			public UInt16 e_res_2;              // Reserved words
			public UInt16 e_res_3;              // Reserved words
			public UInt16 e_oemid;              // OEM identifier (for e_oeminfo)
			public UInt16 e_oeminfo;            // OEM information; e_oemid specific
			public UInt16 e_res2_0;             // Reserved words
			public UInt16 e_res2_1;             // Reserved words
			public UInt16 e_res2_2;             // Reserved words
			public UInt16 e_res2_3;             // Reserved words
			public UInt16 e_res2_4;             // Reserved words
			public UInt16 e_res2_5;             // Reserved words
			public UInt16 e_res2_6;             // Reserved words
			public UInt16 e_res2_7;             // Reserved words
			public UInt16 e_res2_8;             // Reserved words
			public UInt16 e_res2_9;             // Reserved words
			public UInt32 e_lfanew;             // File address of new exe header
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct IMAGE_DATA_DIRECTORY
		{
			public UInt32 VirtualAddress;
			public UInt32 Size;
		}

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		public struct IMAGE_OPTIONAL_HEADER32
		{
			public UInt16 Magic;
			public Byte MajorLinkerVersion;
			public Byte MinorLinkerVersion;
			public UInt32 SizeOfCode;
			public UInt32 SizeOfInitializedData;
			public UInt32 SizeOfUninitializedData;
			public UInt32 AddressOfEntryPoint;
			public UInt32 BaseOfCode;
			public UInt32 BaseOfData;
			public UInt32 ImageBase;
			public UInt32 SectionAlignment;
			public UInt32 FileAlignment;
			public UInt16 MajorOperatingSystemVersion;
			public UInt16 MinorOperatingSystemVersion;
			public UInt16 MajorImageVersion;
			public UInt16 MinorImageVersion;
			public UInt16 MajorSubsystemVersion;
			public UInt16 MinorSubsystemVersion;
			public UInt32 Win32VersionValue;
			public UInt32 SizeOfImage;
			public UInt32 SizeOfHeaders;
			public UInt32 CheckSum;
			public UInt16 Subsystem;
			public UInt16 DllCharacteristics;
			public UInt32 SizeOfStackReserve;
			public UInt32 SizeOfStackCommit;
			public UInt32 SizeOfHeapReserve;
			public UInt32 SizeOfHeapCommit;
			public UInt32 LoaderFlags;
			public UInt32 NumberOfRvaAndSizes;

			public IMAGE_DATA_DIRECTORY ExportTable;
			public IMAGE_DATA_DIRECTORY ImportTable;
			public IMAGE_DATA_DIRECTORY ResourceTable;
			public IMAGE_DATA_DIRECTORY ExceptionTable;
			public IMAGE_DATA_DIRECTORY CertificateTable;
			public IMAGE_DATA_DIRECTORY BaseRelocationTable;
			public IMAGE_DATA_DIRECTORY Debug;
			public IMAGE_DATA_DIRECTORY Architecture;
			public IMAGE_DATA_DIRECTORY GlobalPtr;
			public IMAGE_DATA_DIRECTORY TLSTable;
			public IMAGE_DATA_DIRECTORY LoadConfigTable;
			public IMAGE_DATA_DIRECTORY BoundImport;
			public IMAGE_DATA_DIRECTORY IAT;
			public IMAGE_DATA_DIRECTORY DelayImportDescriptor;
			public IMAGE_DATA_DIRECTORY CLRRuntimeHeader;
			public IMAGE_DATA_DIRECTORY Reserved;
		}

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		public struct IMAGE_OPTIONAL_HEADER64
		{
			public UInt16 Magic;
			public Byte MajorLinkerVersion;
			public Byte MinorLinkerVersion;
			public UInt32 SizeOfCode;
			public UInt32 SizeOfInitializedData;
			public UInt32 SizeOfUninitializedData;
			public UInt32 AddressOfEntryPoint;
			public UInt32 BaseOfCode;
			public UInt64 ImageBase;
			public UInt32 SectionAlignment;
			public UInt32 FileAlignment;
			public UInt16 MajorOperatingSystemVersion;
			public UInt16 MinorOperatingSystemVersion;
			public UInt16 MajorImageVersion;
			public UInt16 MinorImageVersion;
			public UInt16 MajorSubsystemVersion;
			public UInt16 MinorSubsystemVersion;
			public UInt32 Win32VersionValue;
			public UInt32 SizeOfImage;
			public UInt32 SizeOfHeaders;
			public UInt32 CheckSum;
			public UInt16 Subsystem;
			public UInt16 DllCharacteristics;
			public UInt64 SizeOfStackReserve;
			public UInt64 SizeOfStackCommit;
			public UInt64 SizeOfHeapReserve;
			public UInt64 SizeOfHeapCommit;
			public UInt32 LoaderFlags;
			public UInt32 NumberOfRvaAndSizes;

			public IMAGE_DATA_DIRECTORY ExportTable;
			public IMAGE_DATA_DIRECTORY ImportTable;
			public IMAGE_DATA_DIRECTORY ResourceTable;
			public IMAGE_DATA_DIRECTORY ExceptionTable;
			public IMAGE_DATA_DIRECTORY CertificateTable;
			public IMAGE_DATA_DIRECTORY BaseRelocationTable;
			public IMAGE_DATA_DIRECTORY Debug;
			public IMAGE_DATA_DIRECTORY Architecture;
			public IMAGE_DATA_DIRECTORY GlobalPtr;
			public IMAGE_DATA_DIRECTORY TLSTable;
			public IMAGE_DATA_DIRECTORY LoadConfigTable;
			public IMAGE_DATA_DIRECTORY BoundImport;
			public IMAGE_DATA_DIRECTORY IAT;
			public IMAGE_DATA_DIRECTORY DelayImportDescriptor;
			public IMAGE_DATA_DIRECTORY CLRRuntimeHeader;
			public IMAGE_DATA_DIRECTORY Reserved;
		}

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		public struct IMAGE_FILE_HEADER
		{
			public UInt16 Machine;
			public UInt16 NumberOfSections;
			public UInt32 TimeDateStamp;
			public UInt32 PointerToSymbolTable;
			public UInt32 NumberOfSymbols;
			public UInt16 SizeOfOptionalHeader;
			public UInt16 Characteristics;
		}

		[StructLayout(LayoutKind.Explicit)]
		public struct IMAGE_SECTION_HEADER
		{
			[FieldOffset(0)]
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
			public char[] Name;
			[FieldOffset(8)]
			public UInt32 VirtualSize;
			[FieldOffset(12)]
			public UInt32 VirtualAddress;
			[FieldOffset(16)]
			public UInt32 SizeOfRawData;
			[FieldOffset(20)]
			public UInt32 PointerToRawData;
			[FieldOffset(24)]
			public UInt32 PointerToRelocations;
			[FieldOffset(28)]
			public UInt32 PointerToLinenumbers;
			[FieldOffset(32)]
			public UInt16 NumberOfRelocations;
			[FieldOffset(34)]
			public UInt16 NumberOfLinenumbers;
			[FieldOffset(36)]
			public DataSectionFlags Characteristics;

			public string Section
			{
				get { return new string(Name); }
			}
		}

		[Flags]
		public enum DataSectionFlags : uint
		{
			TypeReg = 0x00000000,
			TypeDsect = 0x00000001,
			TypeNoLoad = 0x00000002,
			TypeGroup = 0x00000004,
			TypeNoPadded = 0x00000008,
			TypeCopy = 0x00000010,
			ContentCode = 0x00000020,
			ContentInitializedData = 0x00000040,
			ContentUninitializedData = 0x00000080,
			LinkOther = 0x00000100,
			LinkInfo = 0x00000200,
			TypeOver = 0x00000400,
			LinkRemove = 0x00000800,
			LinkComDat = 0x00001000,
			NoDeferSpecExceptions = 0x00004000,
			RelativeGP = 0x00008000,
			MemPurgeable = 0x00020000,
			Memory16Bit = 0x00020000,
			MemoryLocked = 0x00040000,
			MemoryPreload = 0x00080000,
			Align1Bytes = 0x00100000,
			Align2Bytes = 0x00200000,
			Align4Bytes = 0x00300000,
			Align8Bytes = 0x00400000,
			Align16Bytes = 0x00500000,
			Align32Bytes = 0x00600000,
			Align64Bytes = 0x00700000,
			Align128Bytes = 0x00800000,
			Align256Bytes = 0x00900000,
			Align512Bytes = 0x00A00000,
			Align1024Bytes = 0x00B00000,
			Align2048Bytes = 0x00C00000,
			Align4096Bytes = 0x00D00000,
			Align8192Bytes = 0x00E00000,
			LinkExtendedRelocationOverflow = 0x01000000,
			MemoryDiscardable = 0x02000000,
			MemoryNotCached = 0x04000000,
			MemoryNotPaged = 0x08000000,
			MemoryShared = 0x10000000,
			MemoryExecute = 0x20000000,
			MemoryRead = 0x40000000,
			MemoryWrite = 0x80000000
		}

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		public struct IMAGE_DEBUG_DIRECTORY
		{
			public UInt32 Characteristics;
			public UInt32 TimeDateStamp;
			public UInt16 MajorVersion;
			public UInt16 MinorVersion;
			public UInt32 Type;
			public UInt32 SizeOfData;
			public UInt32 AddressOfRawData;
			public UInt32 PointerToRawData;
		}

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		public struct IMAGE_DEBUG_DIRECTORY_RAW
		{
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
			public char[] format;
			public Guid guid;
			public uint age;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 255)]
			public char[] name;
		}

		#endregion
	}
}