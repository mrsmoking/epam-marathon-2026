@ui @L1
Feature: Group pre-order screen

  The owner opens the group pre-order of their booking and sees who has joined,
  what each guest has chosen and what the restaurant will get. Only the carts
  the guests have submitted go into the order, so the grand total on the screen
  counts only those, and the owner is reminded about the carts still open.

  Background:
    Given the restaurant's menu has the dishes:
      | dish       | price |
      | Borscht    | 12.50 |
      | Khachapuri | 9.00  |
    And a customer has booked a table for 4 guests

  Rule: The owner sees every participant with their dishes and totals

    @smoke
    Scenario: The owner sees what each guest has chosen
      Given "Anna" has joined the group
      And "Anna" has added 2 "Borscht" to the cart
      And "Anna" has added 1 "Khachapuri" to the cart
      And "Anna" has submitted the cart
      And "Ben" has joined the group
      And "Ben" has added 1 "Khachapuri" to the cart
      When the owner looks at the participants of the group
      Then the owner sees the participants:
        | participant | dishes                      | total | cart          |
        | the owner   | nothing                     | 0     | not submitted |
        | Anna        | 2 x Borscht, 1 x Khachapuri | 34.00 | submitted     |
        | Ben         | 1 x Khachapuri              | 9.00  | not submitted |
      And the grand total of the submitted carts is 34.00
      And the owner is reminded that 2 participants have not submitted their cart

    Scenario: A guest who has not chosen anything yet is listed too
      Given "Anna" has joined the group
      When the owner looks at the participants of the group
      Then the owner sees the participants:
        | participant | dishes  | total | cart          |
        | the owner   | nothing | 0     | not submitted |
        | Anna        | nothing | 0     | not submitted |
      And the owner is told nobody has added any dishes yet

    # Decided for this implementation (see README): a group nobody has joined still lists the owner.
    Scenario: A group nobody has joined yet lists only the owner
      When the owner looks at the participants of the group
      Then the owner sees the participants:
        | participant | dishes  | total | cart          |
        | the owner   | nothing | 0     | not submitted |
      And the grand total of the submitted carts is 0

    Scenario: The owner sees a guest's changes after refreshing
      Given "Anna" has joined the group
      And "Anna" has added 2 "Borscht" to the cart
      When the owner looks at the participants of the group
      And "Anna" adds 1 "Khachapuri" to the cart
      And "Anna" submits the cart
      And the owner refreshes the participants
      Then the owner sees the participants:
        | participant | dishes                      | total | cart          |
        | the owner   | nothing                     | 0     | not submitted |
        | Anna        | 2 x Borscht, 1 x Khachapuri | 34.00 | submitted     |
      And the grand total of the submitted carts is 34.00

  Rule: The group has to exist

    @negative
    Scenario: The owner opens a group that does not exist
      When the owner looks at the participants of a group that does not exist
      Then the owner is told the group could not be found
