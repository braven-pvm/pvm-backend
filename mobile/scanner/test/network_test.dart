import 'dart:async';

import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:scanner/device/network.dart';

void main() {
  test('wifi_mobile_or_another_network_counts_none_and_bluetooth_do_not', () {
    expect(hasNetwork([ConnectivityResult.wifi]), isTrue);
    expect(hasNetwork([ConnectivityResult.mobile]), isTrue);
    expect(hasNetwork([ConnectivityResult.none, ConnectivityResult.vpn]), isTrue);
    expect(hasNetwork([ConnectivityResult.none]), isFalse);
    expect(hasNetwork([ConnectivityResult.bluetooth]), isFalse);
    expect(hasNetwork(const []), isFalse);
  });

  test('a_change_to_a_usable_network_gives_one_wake_each_time', () async {
    final changes = StreamController<List<ConnectivityResult>>();
    final wakes = networkChanges(changes.stream).length;

    changes
      ..add([ConnectivityResult.none])
      ..add([ConnectivityResult.wifi])
      ..add([ConnectivityResult.none])
      ..add([ConnectivityResult.mobile])
      ..add([ConnectivityResult.wifi]); // Another Wi-Fi.
    unawaited(changes.close());

    expect(await wakes, 3);
  });
}
