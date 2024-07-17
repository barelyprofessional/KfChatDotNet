﻿// <auto-generated />
using System;
using KfChatDotNetKickBot;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace KfChatDotNetKickBot.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240714132259_Initial")]
    partial class Initial
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "8.0.7");

            modelBuilder.Entity("KfChatDotNetKickBot.Models.DbModels.JuicerDbModel", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<float>("Amount")
                        .HasColumnType("REAL");

                    b.Property<DateTimeOffset>("JuicedAt")
                        .HasColumnType("TEXT");

                    b.Property<int>("UserId")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("Juicers");
                });

            modelBuilder.Entity("KfChatDotNetKickBot.Models.DbModels.UserDbModel", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<bool>("Ignored")
                        .HasColumnType("INTEGER");

                    b.Property<int>("KfId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("KfUsername")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("UserRight")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.ToTable("Users");
                });

            modelBuilder.Entity("KfChatDotNetKickBot.Models.DbModels.JuicerDbModel", b =>
                {
                    b.HasOne("KfChatDotNetKickBot.Models.DbModels.UserDbModel", "User")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("User");
                });
#pragma warning restore 612, 618
        }
    }
}