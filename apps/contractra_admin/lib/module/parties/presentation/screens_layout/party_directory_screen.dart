import 'package:flutter/material.dart';

import '../../../../core/language/locales.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../widget/empty_state_widget.dart';
import '../../data_layer/models/mock_parties.dart';
import '../../data_layer/models/party_summary.dart';
import '../widgets/party_list_item_widget.dart';

/// Read-only party list. Backed by [mockParties] until a Parties read API
/// exists; no create/edit/delete by design.
class PartyDirectoryScreen extends StatelessWidget {
  const PartyDirectoryScreen({super.key, this.parties = mockParties});

  final List<PartySummary> parties;

  @override
  Widget build(BuildContext context) {
    if (parties.isEmpty) {
      return EmptyStateWidget(
        icon: Icons.groups_outlined,
        title: 'partiesEmptyTitle'.tr,
        message: 'partiesEmptyMessage'.tr,
      );
    }

    return ListView.separated(
      padding: AppSpacing.allLg,
      itemCount: parties.length,
      separatorBuilder: (_, _) => SizedBox(height: AppSpacing.sm),
      itemBuilder: (_, index) => PartyListItemWidget(party: parties[index]),
    );
  }
}
