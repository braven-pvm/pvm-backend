import 'package:flutter_test/flutter_test.dart';
import 'package:scanner/api/device_api.dart';
import 'package:scanner/device/staff.dart';
import 'package:scanner/settings/app_settings.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:shared_preferences_platform_interface/in_memory_shared_preferences_async.dart';
import 'package:shared_preferences_platform_interface/shared_preferences_async_platform_interface.dart';

void main() {
  const anna = StaffMember(id: 's-1', name: 'Anna');
  const thabo = StaffMember(id: 's-2', name: 'Thabo');

  test('a_remembered_name_that_is_still_listed_is_used_without_asking', () {
    final choice = StaffChoice.of(const [anna, thabo], 's-2');

    expect(choice.mustAsk, isFalse);
    expect(choice.staff, thabo);
  });

  test('a_remembered_name_that_is_no_longer_listed_is_asked_again', () {
    final choice = StaffChoice.of(const [anna], 's-2');

    expect(choice.mustAsk, isTrue);
    expect(choice.staff, isNull);
  });

  test('without_a_remembered_name_the_app_asks', () {
    expect(StaffChoice.of(const [anna], null).mustAsk, isTrue);
  });

  test('an_empty_list_skips_the_question', () {
    final choice = StaffChoice.of(const [], 's-2');

    expect(choice.mustAsk, isFalse);
    expect(choice.staff, isNull);
  });

  test('the_chosen_staff_id_is_remembered_across_restarts', () async {
    SharedPreferencesAsyncPlatform.instance = InMemorySharedPreferencesAsync.empty();

    await AppSettings(SharedPreferencesAsync()).setStaffId('s-1');

    expect(await AppSettings(SharedPreferencesAsync()).staffId(), 's-1');
    await AppSettings(SharedPreferencesAsync()).setStaffId(null);
    expect(await AppSettings(SharedPreferencesAsync()).staffId(), isNull);
  });
}
