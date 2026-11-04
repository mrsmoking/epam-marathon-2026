@api @L3
Feature: Removing a participant from the group pre-order

  The owner can take a guest out of the group pre-order, together with the
  guest's cart, and then sees the updated list and totals. Only a guest of this
  very group can be removed, only while the pre-order has not been sent, and
  only by the owner.

  Background:
    Given the restaurant's menu has the dishes:
      | dish       | price |
      | Borscht    | 12.50 |
      | Khachapuri | 9.00  |
    And a customer has booked a table for 4 guests
    And "Anna" has joined the group
    And "Anna" has added 2 "Borscht" to the cart
    And "Anna" has submitted the cart
    And "Ben" has joined the group
    And "Ben" has added 1 "Khachapuri" to the cart

  Rule: The owner can remove a guest, and the totals follow

    @smoke
    Scenario: The owner removes one guest of several
      Given "Chris" has joined the group
      And "Chris" has added 1 "Borscht" to the cart
      When the owner removes "Ben" from the group
      Then the owner sees the participants:
        | participant | dishes      | total | cart          |
        | the owner   | nothing     | 0     | not submitted |
        | Anna        | 2 x Borscht | 25.00 | submitted     |
        | Chris       | 1 x Borscht | 12.50 | not submitted |
      And the group total is 37.50

    Scenario: The owner removes a guest who has already submitted the cart
      When the owner removes "Anna" from the group
      Then the owner sees the participants:
        | participant | dishes         | total | cart          |
        | the owner   | nothing        | 0     | not submitted |
        | Ben         | 1 x Khachapuri | 9.00  | not submitted |
      And the group total is 9.00

    Scenario: The owner removes the last guest
      Given the owner has removed "Ben" from the group
      When the owner removes "Anna" from the group
      Then the owner sees the participants:
        | participant | dishes  | total | cart          |
        | the owner   | nothing | 0     | not submitted |
      And the group total is 0

  Rule: Only a guest of this group can be removed

    @negative
    Scenario: A guest of another group cannot be removed
      Given another customer has booked a table for 2 guests
      And "Dave" has joined the other group
      When the owner removes "Dave" from the group
      Then the owner is told the guest is not part of this group

    @negative
    Scenario: A participant who does not exist cannot be removed
      When the owner removes a participant who does not exist
      Then the owner is told the participant could not be found

    @negative
    Scenario: The owner cannot be removed from their own group
      When the owner tries to remove themselves from the group
      Then the owner is told the owner cannot be removed

  Rule: Guests can be removed only until the pre-order is sent

    @negative
    Scenario: A guest cannot be removed after the pre-order was sent
      Given the owner has pre-ordered 1 "Khachapuri"
      And the owner has submitted the group pre-order
      When the owner removes "Anna" from the group
      Then the owner is told the pre-order has already been sent

  Rule: Only the owner can remove participants

    @negative
    Scenario Outline: Someone other than the owner tries to remove a guest
      When <someone> removes "Ben" from the group
      Then they are told <answer>

      Examples: People who are signed in
        | someone          | answer                              |
        | another customer | they may not remove participants    |
        | the waiter       | they may not remove participants    |

      Examples: People who are not signed in
        | someone                        | answer           |
        | a guest of the group           | to sign in first |
        | a visitor who is not signed in | to sign in first |
