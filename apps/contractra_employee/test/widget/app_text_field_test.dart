import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:contractra_employee/core/theme/cubit/theme_cubit.dart';
import 'package:contractra_employee/widget/app_text_field.dart';
import 'package:contractra_employee/utilities/git_it.dart';

void main() {
  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    await GitIt.initGitIt();
  });

  Widget wrap(Widget child, {double width = 360}) => ScreenUtilPlusInit(
        designSize: const Size(1440, 1088),
        builder: (_, __) => MaterialApp(
          home: BlocProvider(
            create: (_) => ThemeCubit(),
            child: Scaffold(
              body: Center(
                child: SizedBox(
                  width: width,
                  child: Material(child: child),
                ),
              ),
            ),
          ),
        ),
      );

  group('AppTextField', () {
    testWidgets('label renders', (tester) async {
      await tester.pumpWidget(wrap(AppTextField(label: 'Name')));

      expect(find.text('Name'), findsOneWidget);
    });

    testWidgets('hint renders when provided', (tester) async {
      await tester.pumpWidget(
        wrap(AppTextField(label: 'Name', hint: 'Enter your name')),
      );

      expect(find.text('Enter your name'), findsOneWidget);
    });

    testWidgets('user input updates the controller', (tester) async {
      final controller = TextEditingController();
      await tester.pumpWidget(
        wrap(AppTextField(label: 'Name', controller: controller)),
      );

      await tester.enterText(find.byType(TextFormField), 'Ahmed');

      expect(controller.text, 'Ahmed');
    });

    testWidgets('onChanged fires', (tester) async {
      String? changed;
      await tester.pumpWidget(
        wrap(AppTextField(label: 'Name', onChanged: (v) => changed = v)),
      );

      await tester.enterText(find.byType(TextFormField), 'Sara');

      expect(changed, 'Sara');
    });

    testWidgets('validator error is displayed', (tester) async {
      final formKey = GlobalKey<FormState>();
      await tester.pumpWidget(wrap(Form(
        key: formKey,
        child: AppTextField(
          label: 'Name',
          validator: (v) => (v == null || v.isEmpty) ? 'Required' : null,
        ),
      )));

      formKey.currentState!.validate();
      await tester.pump();

      expect(find.text('Required'), findsOneWidget);
    });

    testWidgets('disabled state prevents editing', (tester) async {
      final controller = TextEditingController();
      await tester.pumpWidget(wrap(AppTextField(
        label: 'Name',
        controller: controller,
        enabled: false,
      )));

      await tester.enterText(find.byType(TextFormField), 'Blocked');

      expect(controller.text, isNot('Blocked'));
    });

    testWidgets('read-only state prevents editing', (tester) async {
      final controller = TextEditingController(text: 'Fixed');
      await tester.pumpWidget(wrap(AppTextField(
        label: 'Name',
        controller: controller,
        readOnly: true,
      )));

      await tester.enterText(find.byType(TextFormField), 'Changed');

      expect(controller.text, 'Fixed');
    });

    testWidgets('prefix icon renders', (tester) async {
      await tester.pumpWidget(wrap(AppTextField(
        label: 'Amount',
        prefixIcon: Icons.attach_money,
      )));

      expect(find.byIcon(Icons.attach_money), findsOneWidget);
    });

    testWidgets('suffix icon renders', (tester) async {
      await tester.pumpWidget(wrap(AppTextField(
        label: 'Search',
        suffixIcon: Icons.search,
      )));

      expect(find.byIcon(Icons.search), findsOneWidget);
    });

    testWidgets('obscureText works', (tester) async {
      await tester.pumpWidget(wrap(AppTextField(
        label: 'Password',
        obscureText: true,
      )));

      final field = tester.widget<TextField>(find.byType(TextField));
      expect(field.obscureText, isTrue);
    });

    testWidgets('maxLines is respected', (tester) async {
      await tester.pumpWidget(wrap(AppTextField(
        label: 'Notes',
        maxLines: 4,
      )));

      final field = tester.widget<TextField>(find.byType(TextField));
      expect(field.maxLines, 4);
    });

    testWidgets('maxLength is respected', (tester) async {
      await tester.pumpWidget(wrap(AppTextField(
        label: 'Code',
        maxLength: 6,
      )));

      final field = tester.widget<TextField>(find.byType(TextField));
      expect(field.maxLength, 6);
    });

    test('controller and initialValue together trigger assertion', () {
      expect(
        () => AppTextField(
          label: 'Name',
          controller: TextEditingController(),
          initialValue: 'Ahmed',
        ),
        throwsAssertionError,
      );
    });

    test('obscureText with multiple lines triggers assertion', () {
      expect(
        () => AppTextField(
          label: 'Password',
          obscureText: true,
          maxLines: 3,
        ),
        throwsAssertionError,
      );
    });

    testWidgets('onFieldSubmitted is forwarded', (tester) async {
      String? submitted;
      await tester.pumpWidget(wrap(AppTextField(
        label: 'Name',
        onFieldSubmitted: (v) => submitted = v,
      )));

      await tester.enterText(find.byType(TextFormField), 'Omar');
      await tester.testTextInput.receiveAction(TextInputAction.done);
      await tester.pump();

      expect(submitted, 'Omar');
    });

    testWidgets('minimum field interaction height remains field-friendly',
        (tester) async {
      await tester.pumpWidget(wrap(AppTextField(label: 'Name')));

      final fieldSize = tester.getSize(find.byType(TextFormField));

      expect(fieldSize.height, greaterThanOrEqualTo(48));
    });

    testWidgets('RTL Arabic label/input renders without overflow',
        (tester) async {
      await tester.pumpWidget(
        Directionality(
          textDirection: TextDirection.rtl,
          child: wrap(AppTextField(
            label: 'الاسم الكامل',
            hint: 'أدخل اسمك هنا',
          )),
        ),
      );

      expect(tester.takeException(), isNull);
      expect(find.text('الاسم الكامل'), findsOneWidget);
    });
    testWidgets('suffix icon becomes tappable when onSuffixIconPressed is set', (tester) async {
      var taps = 0;
      await tester.pumpWidget(wrap(AppTextField(
        label: 'Password',
        suffixIcon: Icons.visibility_outlined,
        onSuffixIconPressed: () => taps++,
      )));

      await tester.tap(find.byIcon(Icons.visibility_outlined));
      expect(taps, 1);
    });

    testWidgets('errorText is shown', (tester) async {
      await tester.pumpWidget(wrap(const AppTextField(
        label: 'Name',
        errorText: 'Required',
      )));

      expect(find.text('Required'), findsOneWidget);
    });
  });
}
