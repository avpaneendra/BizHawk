use crate::syscall_defs::*;
use crate::*;
use std::io::{Write, Read};
use super::*;

/// stdout, stderr
pub struct SysOutObj {
	pub host_handle: Box<dyn Write>,
}
impl IStateable for SysOutObj {
	fn save_state(&mut self, stream: &mut dyn Write) -> anyhow::Result<()> {
		bin::write_magic(stream, "SysOutObj")?;
		Ok(())
	}
	fn load_state(&mut self, stream: &mut dyn Read) -> anyhow::Result<()> {
		bin::verify_magic(stream, "SysOutObj")?;
		Ok(())
	}
}
impl FileObject for SysOutObj {
	fn can_read(&self) -> bool {
		false
	}
	fn read(&mut self, _buf: &mut [u8]) -> Result<i64, SyscallError> {
		Err(EBADF)
	}
	fn can_write(&self) -> bool {
		true
	}
	fn write(&mut self, buf: &[u8]) -> Result<i64, SyscallError> {
		// do not propogate host errors up to the waterbox!
		let _ = self.host_handle.write_all(buf);
		Ok(buf.len() as i64)
	}
	fn seek(&mut self, _offset: i64, _whence: i32) -> Result<i64, SyscallError> {
		Err(ESPIPE)
	}
	fn truncate(&mut self, _size: i64) -> SyscallResult {
		Err(EINVAL)
	}
	fn stat(&self, statbuff: &mut KStat) -> SyscallResult {
		fill_stat(statbuff, false, true, false, 0)
	}
	fn can_unmount(&self) -> bool {
		false
	}
	fn unmount(self: Box<Self>) -> Vec<u8> {
		panic!()
	}
	fn reset(&mut self) {}
}
