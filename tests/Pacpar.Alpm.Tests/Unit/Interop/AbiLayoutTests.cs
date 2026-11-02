using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.Tests.Unit.Interop;

public sealed class AbiLayoutTests
{
  public AbiLayoutTests()
  {
    Assert.Equal(8, IntPtr.Size);
  }

  [Fact]
  public void ListStruct_HasCorrectLayout()
  {
    Assert.Equal(24, Marshal.SizeOf<_alpm_list_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_list_t>(nameof(_alpm_list_t.data)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<_alpm_list_t>(nameof(_alpm_list_t.prev)).ToInt32());
    Assert.Equal(16, Marshal.OffsetOf<_alpm_list_t>(nameof(_alpm_list_t.next)).ToInt32());
  }

  [Fact]
  public void FileStruct_HasCorrectLayout()
  {
    Assert.Equal(24, Marshal.SizeOf<_alpm_file_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_file_t>(nameof(_alpm_file_t.name)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<_alpm_file_t>(nameof(_alpm_file_t.size)).ToInt32());
    Assert.Equal(16, Marshal.OffsetOf<_alpm_file_t>(nameof(_alpm_file_t.mode)).ToInt32());
  }

  [Fact]
  public void FileListStruct_HasCorrectLayout()
  {
    Assert.Equal(16, Marshal.SizeOf<_alpm_filelist_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_filelist_t>(nameof(_alpm_filelist_t.count)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<_alpm_filelist_t>(nameof(_alpm_filelist_t.files)).ToInt32());
  }

  [Fact]
  public void BackupStruct_HasCorrectLayout()
  {
    Assert.Equal(16, Marshal.SizeOf<_alpm_backup_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_backup_t>(nameof(_alpm_backup_t.name)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<_alpm_backup_t>(nameof(_alpm_backup_t.hash)).ToInt32());
  }

  [Fact]
  public void GroupStruct_HasCorrectLayout()
  {
    Assert.Equal(16, Marshal.SizeOf<_alpm_group_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_group_t>(nameof(_alpm_group_t.name)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<_alpm_group_t>(nameof(_alpm_group_t.packages)).ToInt32());
  }

  [Fact]
  public void DependStruct_HasCorrectLayout()
  {
    Assert.Equal(40, Marshal.SizeOf<_alpm_depend_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_depend_t>(nameof(_alpm_depend_t.name)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<_alpm_depend_t>(nameof(_alpm_depend_t.version)).ToInt32());
    Assert.Equal(16, Marshal.OffsetOf<_alpm_depend_t>(nameof(_alpm_depend_t.desc)).ToInt32());
    Assert.Equal(24, Marshal.OffsetOf<_alpm_depend_t>(nameof(_alpm_depend_t.name_hash)).ToInt32());
    Assert.Equal(32, Marshal.OffsetOf<_alpm_depend_t>(nameof(_alpm_depend_t.mod_)).ToInt32());
  }

  [Fact]
  public void DepMissingStruct_HasCorrectLayout()
  {
    Assert.Equal(24, Marshal.SizeOf<_alpm_depmissing_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_depmissing_t>(nameof(_alpm_depmissing_t.target)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<_alpm_depmissing_t>(nameof(_alpm_depmissing_t.depend)).ToInt32());
    Assert.Equal(16, Marshal.OffsetOf<_alpm_depmissing_t>(nameof(_alpm_depmissing_t.causingpkg)).ToInt32());
  }

  [Fact]
  public void ConflictStruct_HasCorrectLayout()
  {
    Assert.Equal(24, Marshal.SizeOf<_alpm_conflict_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_conflict_t>(nameof(_alpm_conflict_t.package1)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<_alpm_conflict_t>(nameof(_alpm_conflict_t.package2)).ToInt32());
    Assert.Equal(16, Marshal.OffsetOf<_alpm_conflict_t>(nameof(_alpm_conflict_t.reason)).ToInt32());
  }

  [Fact]
  public void FileConflictStruct_HasCorrectLayout()
  {
    Assert.Equal(32, Marshal.SizeOf<_alpm_fileconflict_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_fileconflict_t>(nameof(_alpm_fileconflict_t.target)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<_alpm_fileconflict_t>(nameof(_alpm_fileconflict_t.type_)).ToInt32());
    Assert.Equal(16, Marshal.OffsetOf<_alpm_fileconflict_t>(nameof(_alpm_fileconflict_t.file)).ToInt32());
    Assert.Equal(24, Marshal.OffsetOf<_alpm_fileconflict_t>(nameof(_alpm_fileconflict_t.ctarget)).ToInt32());
  }

  [Fact]
  public void EventStruct_HasCorrectUnionLayoutAndArmOffsets()
  {
    Assert.Equal(40, Marshal.SizeOf<_alpm_event_t>());

    Assert.Equal(0, Marshal.OffsetOf<_alpm_event_t>(nameof(_alpm_event_t.type_)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_event_t>(nameof(_alpm_event_t.any)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_event_t>(nameof(_alpm_event_t.package_operation)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_event_t>(nameof(_alpm_event_t.optdep_removal)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_event_t>(nameof(_alpm_event_t.scriptlet_info)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_event_t>(nameof(_alpm_event_t.database_missing)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_event_t>(nameof(_alpm_event_t.pkgdownload)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_event_t>(nameof(_alpm_event_t.pacnew_created)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_event_t>(nameof(_alpm_event_t.pacsave_created)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_event_t>(nameof(_alpm_event_t.hook)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_event_t>(nameof(_alpm_event_t.hook_run)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_event_t>(nameof(_alpm_event_t.pkg_retrieve)).ToInt32());

    // Package operation arm
    Assert.Equal(24, Marshal.SizeOf<_alpm_event_package_operation_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_event_package_operation_t>(nameof(_alpm_event_package_operation_t.type_)).ToInt32());
    Assert.Equal(4, Marshal.OffsetOf<_alpm_event_package_operation_t>(nameof(_alpm_event_package_operation_t.operation)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<_alpm_event_package_operation_t>(nameof(_alpm_event_package_operation_t.oldpkg)).ToInt32());
    Assert.Equal(16, Marshal.OffsetOf<_alpm_event_package_operation_t>(nameof(_alpm_event_package_operation_t.newpkg)).ToInt32());

    // Pacnew created arm
    Assert.Equal(32, Marshal.SizeOf<_alpm_event_pacnew_created_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_event_pacnew_created_t>(nameof(_alpm_event_pacnew_created_t.type_)).ToInt32());
    Assert.Equal(4, Marshal.OffsetOf<_alpm_event_pacnew_created_t>(nameof(_alpm_event_pacnew_created_t.from_noupgrade)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<_alpm_event_pacnew_created_t>(nameof(_alpm_event_pacnew_created_t.oldpkg)).ToInt32());
    Assert.Equal(16, Marshal.OffsetOf<_alpm_event_pacnew_created_t>(nameof(_alpm_event_pacnew_created_t.newpkg)).ToInt32());
    Assert.Equal(24, Marshal.OffsetOf<_alpm_event_pacnew_created_t>(nameof(_alpm_event_pacnew_created_t.file)).ToInt32());

    // Hook run arm
    Assert.Equal(40, Marshal.SizeOf<_alpm_event_hook_run_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_event_hook_run_t>(nameof(_alpm_event_hook_run_t.type_)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<_alpm_event_hook_run_t>(nameof(_alpm_event_hook_run_t.name)).ToInt32());
    Assert.Equal(16, Marshal.OffsetOf<_alpm_event_hook_run_t>(nameof(_alpm_event_hook_run_t.desc)).ToInt32());
    Assert.Equal(24, Marshal.OffsetOf<_alpm_event_hook_run_t>(nameof(_alpm_event_hook_run_t.position)).ToInt32());
    Assert.Equal(32, Marshal.OffsetOf<_alpm_event_hook_run_t>(nameof(_alpm_event_hook_run_t.total)).ToInt32());

    // Pkg retrieve arm
    Assert.Equal(24, Marshal.SizeOf<_alpm_event_pkg_retrieve_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_event_pkg_retrieve_t>(nameof(_alpm_event_pkg_retrieve_t.type_)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<_alpm_event_pkg_retrieve_t>(nameof(_alpm_event_pkg_retrieve_t.num)).ToInt32());
    Assert.Equal(16, Marshal.OffsetOf<_alpm_event_pkg_retrieve_t>(nameof(_alpm_event_pkg_retrieve_t.total_size)).ToInt32());
  }

  [Fact]
  public void QuestionStruct_HasCorrectUnionLayoutAndArmOffsets()
  {
    Assert.Equal(32, Marshal.SizeOf<_alpm_question_t>());

    Assert.Equal(0, Marshal.OffsetOf<_alpm_question_t>(nameof(_alpm_question_t.type_)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_question_t>(nameof(_alpm_question_t.any)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_question_t>(nameof(_alpm_question_t.install_ignorepkg)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_question_t>(nameof(_alpm_question_t.replace)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_question_t>(nameof(_alpm_question_t.conflict)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_question_t>(nameof(_alpm_question_t.corrupted)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_question_t>(nameof(_alpm_question_t.remove_pkgs)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_question_t>(nameof(_alpm_question_t.select_provider)).ToInt32());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_question_t>(nameof(_alpm_question_t.import_key)).ToInt32());

    // Replace arm
    Assert.Equal(32, Marshal.SizeOf<_alpm_question_replace_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_question_replace_t>(nameof(_alpm_question_replace_t.type_)).ToInt32());
    Assert.Equal(4, Marshal.OffsetOf<_alpm_question_replace_t>(nameof(_alpm_question_replace_t.replace)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<_alpm_question_replace_t>(nameof(_alpm_question_replace_t.oldpkg)).ToInt32());
    Assert.Equal(16, Marshal.OffsetOf<_alpm_question_replace_t>(nameof(_alpm_question_replace_t.newpkg)).ToInt32());
    Assert.Equal(24, Marshal.OffsetOf<_alpm_question_replace_t>(nameof(_alpm_question_replace_t.newdb)).ToInt32());

    // Corrupted arm
    Assert.Equal(24, Marshal.SizeOf<_alpm_question_corrupted_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_question_corrupted_t>(nameof(_alpm_question_corrupted_t.type_)).ToInt32());
    Assert.Equal(4, Marshal.OffsetOf<_alpm_question_corrupted_t>(nameof(_alpm_question_corrupted_t.remove)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<_alpm_question_corrupted_t>(nameof(_alpm_question_corrupted_t.filepath)).ToInt32());
    Assert.Equal(16, Marshal.OffsetOf<_alpm_question_corrupted_t>(nameof(_alpm_question_corrupted_t.reason)).ToInt32());
  }

  [Fact]
  public void DownloadEventStructs_HaveCorrectLayouts()
  {
    Assert.Equal(4, Marshal.SizeOf<_alpm_download_event_init_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_download_event_init_t>(nameof(_alpm_download_event_init_t.optional)).ToInt32());

    Assert.Equal(16, Marshal.SizeOf<_alpm_download_event_progress_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_download_event_progress_t>(nameof(_alpm_download_event_progress_t.downloaded)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<_alpm_download_event_progress_t>(nameof(_alpm_download_event_progress_t.total)).ToInt32());

    Assert.Equal(4, Marshal.SizeOf<_alpm_download_event_retry_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_download_event_retry_t>(nameof(_alpm_download_event_retry_t.resume)).ToInt32());

    Assert.Equal(16, Marshal.SizeOf<_alpm_download_event_completed_t>());
    Assert.Equal(0, Marshal.OffsetOf<_alpm_download_event_completed_t>(nameof(_alpm_download_event_completed_t.total)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<_alpm_download_event_completed_t>(nameof(_alpm_download_event_completed_t.result)).ToInt32());
  }

  [Fact]
  public void VaListTagStruct_HasCorrectLayout()
  {
    Assert.Equal(24, Marshal.SizeOf<__va_list_tag>());
    Assert.Equal(0, Marshal.OffsetOf<__va_list_tag>(nameof(__va_list_tag.gp_offset)).ToInt32());
    Assert.Equal(4, Marshal.OffsetOf<__va_list_tag>(nameof(__va_list_tag.fp_offset)).ToInt32());
    Assert.Equal(8, Marshal.OffsetOf<__va_list_tag>(nameof(__va_list_tag.overflow_arg_area)).ToInt32());
    Assert.Equal(16, Marshal.OffsetOf<__va_list_tag>(nameof(__va_list_tag.reg_save_area)).ToInt32());
  }
}
