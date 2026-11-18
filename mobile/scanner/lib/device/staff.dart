import '../api/device_api.dart';

/// Whether the app must ask "Who is scanning?" before the next scan.
class StaffChoice {
  const StaffChoice._(this.mustAsk, this.staff);

  /// [staff] is the active list from the device config; [rememberedId] the last choice.
  /// - An empty list: no question, no name.
  /// - The remembered name is still listed: no question, that name.
  /// - Otherwise: ask.
  factory StaffChoice.of(List<StaffMember> staff, String? rememberedId) {
    if (staff.isEmpty) {
      return const StaffChoice._(false, null);
    }
    for (final member in staff) {
      if (member.id == rememberedId) {
        return StaffChoice._(false, member);
      }
    }
    return const StaffChoice._(true, null);
  }

  final bool mustAsk;

  /// The name to use without asking, if any.
  final StaffMember? staff;
}
